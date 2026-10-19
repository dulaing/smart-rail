import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/food/order_api.dart';
import 'package:passenger_app/food/order_models.dart';
import 'package:passenger_app/food/orders_controller.dart';
import 'package:passenger_app/food/request_store.dart';

import 'order_support.dart';

Future<(TestAccount, FakeOrderSource, MemoryFoodRequestStore, FoodOrdersController)> signedIn({
  String id = 'acc-1',
}) async {
  final account = TestAccount();
  final api = FakeOrderSource();
  final store = MemoryFoodRequestStore();
  final orders = ordersFor(account, api, store: store);
  await account.signInAs(id);
  return (account, api, store, orders);
}

void main() {
  test('the request body has only the five contract fields, never an ETA, cutoff or account', () async {
    final (_, api, _, orders) = await signedIn();
    api.placeScript.add(() async => reply(201, orderJson()));
    await orders.submit(sampleDraft());
    final body = api.placed.single;
    expect(body.keys.toSet(), {'clientRequestKey', 'collectionPointId', 'journeyId', 'pickupStopId', 'items'});
    expect(body['items'], [
      {'menuItemId': 'pdn-lamprais', 'quantity': 2},
    ]);
    for (final forbidden in ['eta', 'estimatedArrival', 'cutoff', 'cutoffMinutes', 'accountId', 'orderBy', 'pickupTime']) {
      expect(body.containsKey(forbidden), isFalse, reason: forbidden);
    }
    orders.dispose();
  });

  test('items are canonically ordered, so the same basket is the same bytes', () {
    final a = CanonicalOrder(
      collectionPointId: 'p',
      journeyId: 'j',
      pickupStopId: 's',
      items: [DraftItem('b', 1), DraftItem('a', 2)],
    );
    final b = CanonicalOrder(
      collectionPointId: 'p',
      journeyId: 'j',
      pickupStopId: 's',
      items: [DraftItem('a', 2), DraftItem('b', 1)],
    );
    expect(a.toRequestJson('k'), b.toRequestJson('k'));
    expect(a.items.map((i) => i.menuItemId), ['a', 'b']);
  });

  test('the request is saved under the account before the first send', () async {
    final (_, api, store, orders) = await signedIn();
    PendingFoodRequest? seenAtSend;
    api.onPlace = (_) => seenAtSend = PendingFoodRequest.decode(store.entries['acc-1']!);
    api.placeScript.add(() async => reply(201, orderJson()));
    await orders.submit(sampleDraft());
    expect(seenAtSend, isNotNull, reason: 'saved before sending');
    expect(seenAtSend!.clientRequestKey, api.placed.single['clientRequestKey']);
    expect(store.entries.keys, isNot(contains('acc-2')));
    orders.dispose();
  });

  test('a request that cannot be saved is not sent', () async {
    final (_, api, store, orders) = await signedIn();
    store.failWrites = true;
    await orders.submit(sampleDraft());
    expect(api.placed, isEmpty);
    expect(orders.phase, OrderPhase.storageFailed);
    orders.dispose();
  });

  test('repeated taps while the first send is in flight use one request', () async {
    final (_, api, _, orders) = await signedIn();
    final gate = Completer<OrderReply>();
    api.placeScript.add(() => gate.future);
    final first = orders.submit(sampleDraft());
    await orders.submit(sampleDraft());
    await orders.submit(sampleDraft());
    gate.complete(reply(201, orderJson()));
    await first;
    expect(api.placed, hasLength(1));
    orders.dispose();
  });

  test('201 confirms: the order and pickup code are shown and the saved request is cleared', () async {
    final (_, api, store, orders) = await signedIn();
    api.placeScript.add(() async => reply(201, orderJson()));
    var committed = 0;
    await orders.submit(sampleDraft(), onCommitted: () => committed++);
    expect(orders.phase, OrderPhase.none);
    expect(orders.order!.status, 'ACCEPTED');
    expect(orders.order!.pickupCode, '482915');
    expect(orders.viewing, isTrue);
    expect(committed, 1);
    expect(store.entries, isEmpty);
    orders.dispose();
  });

  test('a stored request never contains a pickup code', () async {
    final (_, api, store, orders) = await signedIn();
    api.placeScript.add(() async => reply(202, orderJson(status: 'PLACING', pickupCode: null)));
    await orders.submit(sampleDraft());
    final saved = store.entries['acc-1']!;
    expect(saved.contains('482915'), isFalse);
    expect(saved.toLowerCase().contains('pickupcode'), isFalse);
    orders.dispose();
  });

  test('202 keeps one pending request with its stable link and says the outcome is unknown', () async {
    final (_, api, store, orders) = await signedIn();
    api.placeScript.add(() async => reply(202, orderJson(status: 'PLACING')));
    var committed = 0;
    await orders.submit(sampleDraft(), onCommitted: () => committed++);
    expect(orders.phase, OrderPhase.uncertain);
    expect(orders.request!.orderId, 'order-1');
    expect(orders.order, isNull, reason: 'no confirmed order and so no code');
    expect(committed, 1);
    expect(PendingFoodRequest.decode(store.entries['acc-1']!).orderId, 'order-1');
    orders.dispose();
  });

  test('while a request is unresolved a new submit, however edited, mints no second key', () async {
    final (_, api, _, orders) = await signedIn();
    api.placeScript.add(() async => reply(202, orderJson(status: 'PLACING')));
    await orders.submit(sampleDraft());
    final key = orders.request!.clientRequestKey;
    await orders.submit(sampleDraft(quantity: 5));
    await orders.submit(sampleDraft(quantity: 7));
    expect(api.placed, hasLength(1));
    expect(orders.request!.clientRequestKey, key);
    expect(orders.request!.order.items.single.quantity, 2, reason: 'the frozen payload is not edited');
    orders.dispose();
  });

  test('a lost reply keeps the key and recovery replays the identical payload', () async {
    final (_, api, store, orders) = await signedIn();
    api.placeScript
      ..add(() async => const OrderReply.lost())
      ..add(() async => reply(200, orderJson()));
    await orders.submit(sampleDraft());
    expect(orders.phase, OrderPhase.uncertain);
    expect(store.entries, hasLength(1));
    await orders.recover();
    expect(api.placed, hasLength(2));
    expect(api.placed.last, api.placed.first, reason: 'same key, same payload');
    expect(orders.order!.status, 'ACCEPTED');
    expect(store.entries, isEmpty);
    orders.dispose();
  });

  test('422 is a conclusive refusal: nothing is kept and a deliberate retry gets a new key', () async {
    final (_, api, store, orders) = await signedIn();
    api.placeScript
      ..add(() async => problem(422, 'STOCK_INSUFFICIENT'))
      ..add(() async => reply(201, orderJson()));
    var committed = 0;
    await orders.submit(sampleDraft(), onCommitted: () => committed++);
    expect(orders.phase, OrderPhase.refused);
    expect(orders.refusalCode, 'STOCK_INSUFFICIENT');
    expect(orders.request, isNull);
    expect(store.entries, isEmpty);
    expect(committed, 0, reason: 'the basket stays for editing');
    orders.dismissProblem();
    await orders.submit(sampleDraft());
    expect(api.placed, hasLength(2));
    expect(api.placed.last['clientRequestKey'], isNot(api.placed.first['clientRequestKey']));
    orders.dispose();
  });

  test('a refusal that already created the link still clears the request', () async {
    final (_, api, store, orders) = await signedIn();
    api.placeScript.add(() async => problem(422, 'CUTOFF_PASSED', orderId: 'order-9'));
    await orders.submit(sampleDraft());
    expect(orders.refusalCode, 'CUTOFF_PASSED');
    expect(store.entries, isEmpty);
    orders.dispose();
  });

  test('401 on the very first attempt is a clean refusal; after a lost attempt the request is kept', () async {
    final (_, api, store, orders) = await signedIn();
    api.placeScript.add(() async => problem(401, 'UNAUTHORIZED'));
    await orders.submit(sampleDraft());
    expect(orders.phase, OrderPhase.sessionEnded);
    expect(store.entries, isEmpty, reason: 'nothing was ever created');
    orders.dismissProblem();

    api.placeScript
      ..add(() async => const OrderReply.lost())
      ..add(() async => problem(401, 'UNAUTHORIZED'));
    await orders.submit(sampleDraft());
    await orders.recover();
    expect(orders.phase, OrderPhase.sessionEnded);
    expect(store.entries, hasLength(1), reason: 'the lost attempt may have been processed');
    orders.dispose();
  });

  test('403 says the account may not order', () async {
    final (_, api, _, orders) = await signedIn();
    api.placeScript.add(() async => problem(403, 'FORBIDDEN'));
    await orders.submit(sampleDraft());
    expect(orders.phase, OrderPhase.forbidden);
    orders.dispose();
  });

  test('an account without the orders permission is told so before submitting', () async {
    final account = TestAccount();
    final orders = ordersFor(account, FakeOrderSource());
    await account.signInAs('acc-1', permissions: ['saved-routes.manage']);
    expect(orders.mayOrder, isFalse);
    orders.dispose();
  });

  test('signing out drops the order, the pickup code and any pending request from memory at once', () async {
    final (account, api, store, orders) = await signedIn();
    api.placeScript.add(() async => reply(201, orderJson()));
    await orders.submit(sampleDraft());
    expect(orders.order, isNotNull);
    await account.signOut();
    expect(orders.order, isNull);
    expect(orders.request, isNull);
    expect(orders.signedIn, isFalse);
    expect(orders.viewing, isFalse);
    expect(store.entries, isEmpty);
    orders.dispose();
  });

  test('a late answer for the previous account is ignored after a switch', () async {
    final (account, api, store, orders) = await signedIn(id: 'acc-1');
    final gate = Completer<OrderReply>();
    api.placeScript.add(() => gate.future);
    final inFlight = orders.submit(sampleDraft());
    await Future<void>.delayed(Duration.zero);
    await account.signOut();
    await account.signInAs('acc-2');
    gate.complete(reply(201, orderJson()));
    await inFlight;
    expect(orders.accountId, 'acc-2');
    expect(orders.order, isNull, reason: 'acc-1 answer must not show for acc-2');
    expect(orders.request, isNull);
    expect(orders.phase, OrderPhase.none);
    expect(store.entries.containsKey('acc-2'), isFalse);
    orders.dispose();
  });

  test('another account never loads or submits a previous account\'s pending payload', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    final store = MemoryFoodRequestStore();
    final orders = ordersFor(account, api, store: store);
    await account.signInAs('acc-1');
    api.placeScript.add(() async => reply(202, orderJson(status: 'PLACING')));
    await orders.submit(sampleDraft());
    expect(store.entries.keys, ['acc-1']);

    await account.signOut();
    await account.signInAs('acc-2');
    expect(orders.request, isNull);
    api.placed.clear();
    await orders.recover();
    expect(api.placed, isEmpty, reason: 'nothing of acc-1 is replayed for acc-2');
    expect(orders.phase, OrderPhase.none);
    orders.dispose();
  });
}
