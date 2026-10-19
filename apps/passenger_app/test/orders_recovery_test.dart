import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/food/order_api.dart';
import 'package:passenger_app/food/orders_controller.dart';
import 'package:passenger_app/food/request_store.dart';

import 'order_support.dart';

/// A server that honours the idempotency contract: one order per (account, key). It can commit an
/// order and then lose the reply, which is the failure the app must survive.
class IdempotentServer {
  final Map<String, Map<String, dynamic>> byKey = {};
  int created = 0;
  bool loseNextReply = false;
  String statusOnCreate = 'ACCEPTED';

  Scripted place(FakeOrderSource api) => () async {
    final body = api.placed.last;
    final key = body['clientRequestKey'] as String;
    final existing = byKey[key];
    final Map<String, dynamic> order;
    var status = 201;
    if (existing != null) {
      order = existing;
      status = existing['status'] == 'PLACING' ? 202 : 200;
    } else {
      created++;
      order = orderJson(id: 'order-$created', status: statusOnCreate, pickupCode: '${100000 + created}');
      byKey[key] = order;
      if (statusOnCreate == 'PLACING') status = 202;
    }
    if (loseNextReply) {
      loseNextReply = false;
      return const OrderReply.lost();
    }
    return reply(status, order);
  };

  /// The provider answers: a PLACING link becomes confirmed.
  void settle({String status = 'ACCEPTED'}) {
    for (final entry in byKey.entries.toList()) {
      byKey[entry.key] = orderJson(id: entry.value['id'] as String, status: status, sequence: 2);
    }
  }

  Scripted read(String id) => () async {
    final found = byKey.values.where((o) => o['id'] == id).firstOrNull;
    return found == null ? problem(404, 'ORDER_NOT_FOUND') : reply(200, found);
  };
}

void main() {
  test('after a lost reply the identical payload is replayed and exactly one order exists', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    final server = IdempotentServer();
    api.fallback = () async => server.place(api)();
    final orders = ordersFor(account, api);
    await account.signInAs('acc-1');

    server.loseNextReply = true; // committed, but the reply is lost before the app learns the link ID
    await orders.submit(sampleDraft());
    expect(orders.phase, OrderPhase.uncertain);
    expect(orders.request!.orderId, isNull);
    expect(server.created, 1);

    await orders.recover();
    expect(server.created, 1, reason: 'the replay found the same order');
    expect(api.placed.map((b) => b['clientRequestKey']).toSet(), hasLength(1));
    expect(api.placed.first, api.placed.last);
    expect(orders.order!.id, 'order-1');
    orders.dispose();
  });

  test('a lost request before the provider commit is also recovered by the same key', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    final server = IdempotentServer();
    api.placeScript.add(() async => const OrderReply.lost()); // never reached the server
    api.fallback = () async => server.place(api)();
    final orders = ordersFor(account, api);
    await account.signInAs('acc-1');

    await orders.submit(sampleDraft());
    expect(server.created, 0);
    await orders.recover();
    expect(server.created, 1);
    expect(orders.order, isNotNull);
    expect(api.placed.map((b) => b['clientRequestKey']).toSet(), hasLength(1));
    orders.dispose();
  });

  testWidgets('202 is polled every 30 seconds only while visible, and the confirmed order replaces it', (tester) async {
    {
      final account = TestAccount();
      final api = FakeOrderSource();
      final server = IdempotentServer()..statusOnCreate = 'PLACING';
      api.fallback = () async => server.place(api)();
      api.readScript.add(() async => server.read('order-1')());
      final orders = ordersFor(account, api);
      unawaited(account.controller.signIn());
      await tester.pump();
      unawaited(orders.submit(sampleDraft()));
      await tester.pump();
      expect(orders.phase, OrderPhase.uncertain);
      expect(orders.request!.orderId, 'order-1');

      await tester.pump(const Duration(minutes: 2));
      expect(api.reads, isEmpty, reason: 'nothing polls while the Food tab is not visible');

      orders.setVisible(true);
      await tester.pump();
      expect(api.reads, ['order-1'], reason: 'becoming visible checks at once');
      api.fallback = () async => const OrderReply.lost();
      api.readScript.addAll([() async => server.read('order-1')(), () async => server.read('order-1')()]);
      await tester.pump(const Duration(seconds: 30));
      expect(api.reads, hasLength(2));
      expect(orders.phase, OrderPhase.uncertain, reason: 'still PLACING; never declared rejected');
      expect(orders.order, isNull);

      server.settle();
      await tester.pump(const Duration(seconds: 30));
      expect(orders.phase, OrderPhase.none);
      expect(orders.order!.status, 'ACCEPTED');
      expect(orders.order!.pickupCode, isNotNull);
      expect(orders.request, isNull);
      final readsAfterConfirm = api.reads.length;
      await tester.pump(const Duration(minutes: 2));
      expect(api.reads, hasLength(readsAfterConfirm), reason: 'no more polling once resolved');

      orders.setVisible(false);
      orders.dispose();
    }
  });

  testWidgets('hiding Food stops polling and showing it again refreshes at once (resume)', (tester) async {
    {
      final account = TestAccount();
      final api = FakeOrderSource();
      api.fallback = () async => reply(202, orderJson(status: 'PLACING'));
      final orders = ordersFor(account, api);
      unawaited(account.controller.signIn());
      await tester.pump();
      unawaited(orders.submit(sampleDraft()));
      await tester.pump();

      orders.setVisible(true);
      await tester.pump();
      final afterShow = api.reads.length;
      orders.setVisible(false);
      await tester.pump(const Duration(minutes: 5));
      expect(api.reads, hasLength(afterShow));
      orders.setVisible(true);
      await tester.pump();
      expect(api.reads, hasLength(afterShow + 1));
      orders.setVisible(false);
      orders.dispose();
    }
  });

  test('a restart reuses the saved key: no link yet replays the payload, a known link is read', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    final store = MemoryFoodRequestStore();
    final server = IdempotentServer();
    api.placeScript.add(() async => const OrderReply.lost());
    api.fallback = () async => server.place(api)();
    var orders = ordersFor(account, api, store: store);
    await account.signInAs('acc-1');
    await orders.submit(sampleDraft());
    final key = api.placed.single['clientRequestKey'];
    orders.dispose(); // the app is killed

    final restarted = ordersFor(account, api, store: store);
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);
    expect(api.placed.map((b) => b['clientRequestKey']).toSet(), {key}, reason: 'same key after restart');
    expect(server.created, 1);
    expect(restarted.order!.id, 'order-1');
    restarted.dispose();

    // A restart while a 202 link is known reads that link instead of placing again.
    final api2 = FakeOrderSource();
    final store2 = MemoryFoodRequestStore();
    api2.placeScript.add(() async => reply(202, orderJson(status: 'PLACING', id: 'order-7')));
    final first = ordersFor(account, api2, store: store2);
    await first.submit(sampleDraft());
    first.dispose();
    api2.readScript.add(() async => reply(200, orderJson(id: 'order-7')));
    orders = ordersFor(account, api2, store: store2);
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);
    expect(api2.placed, hasLength(1), reason: 'never placed a second time');
    expect(api2.reads, ['order-7']);
    expect(orders.order!.id, 'order-7');
    orders.dispose();
  });

  test('a restart shows the unresolved order even though train eligibility has changed since', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    final store = MemoryFoodRequestStore();
    api.placeScript.add(() async => reply(202, orderJson(status: 'PLACING')));
    final first = ordersFor(account, api, store: store);
    await account.signInAs('acc-1');
    await first.submit(sampleDraft());
    first.dispose();

    // The pickup is no longer orderable (nothing here consults eligibility), and the provider is still deciding.
    api.readScript.add(() async => reply(200, orderJson(status: 'PLACING')));
    final restarted = ordersFor(account, api, store: store);
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);
    expect(restarted.phase, OrderPhase.uncertain);
    expect(restarted.viewing, isTrue);
    expect(restarted.request!.orderId, 'order-1');
    restarted.dispose();
  });

  test('a PLACING link that later turns REJECTED ends conclusively and a deliberate retry uses a new key', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    final store = MemoryFoodRequestStore();
    api.placeScript.add(() async => reply(202, orderJson(status: 'PLACING')));
    api.readScript.add(() async => reply(200, orderJson(status: 'REJECTED', reasonCode: 'STOCK_INSUFFICIENT')));
    final orders = ordersFor(account, api, store: store);
    await account.signInAs('acc-1');
    await orders.submit(sampleDraft());
    await orders.recover();
    expect(orders.phase, OrderPhase.refused);
    expect(orders.refusalCode, 'STOCK_INSUFFICIENT');
    expect(store.entries, isEmpty);
    orders.dismissProblem();
    api.placeScript.add(() async => reply(201, orderJson(id: 'order-2')));
    await orders.submit(sampleDraft());
    expect(api.placed.map((b) => b['clientRequestKey']).toSet(), hasLength(2));
    orders.dispose();
  });

  test('a link the server does not know is a conclusive not-found, not a silent success', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    api.placeScript.add(() async => reply(202, orderJson(status: 'PLACING')));
    api.readScript.add(() async => problem(404, 'ORDER_NOT_FOUND'));
    final orders = ordersFor(account, api);
    await account.signInAs('acc-1');
    await orders.submit(sampleDraft());
    await orders.recover();
    expect(orders.phase, OrderPhase.refused);
    expect(orders.refusalCode, 'ORDER_NOT_FOUND');
    orders.dispose();
  });

  test('409 key conflict is shown honestly, never worked around with a fresh key', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    final store = MemoryFoodRequestStore();
    api.placeScript.add(() async => problem(409, 'CLIENT_REQUEST_KEY_IN_USE'));
    final orders = ordersFor(account, api, store: store);
    await account.signInAs('acc-1');
    await orders.submit(sampleDraft());
    final key = api.placed.single['clientRequestKey'];
    expect(orders.phase, OrderPhase.conflict);
    expect(store.entries, hasLength(1));

    api.placeScript.add(() async => problem(409, 'CLIENT_REQUEST_KEY_IN_USE'));
    await orders.recover();
    await orders.submit(sampleDraft());
    expect(api.placed.map((b) => b['clientRequestKey']).toSet(), {key});
    expect(orders.phase, OrderPhase.conflict);

    await orders.discardConflict();
    expect(orders.phase, OrderPhase.none);
    expect(store.entries, isEmpty);
    orders.dispose();
  });

  test('recovery is single-flight: overlapping checks send one request', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    api.placeScript.add(() async => const OrderReply.lost());
    final orders = ordersFor(account, api);
    await account.signInAs('acc-1');
    await orders.submit(sampleDraft());
    final gate = Completer<OrderReply>();
    api.placeScript.add(() => gate.future);
    final a = orders.recover();
    final b = orders.recover();
    final c = orders.recover();
    expect(orders.checking, isTrue);
    gate.complete(reply(201, orderJson()));
    await Future.wait([a, b, c]);
    expect(api.placed, hasLength(2));
    orders.dispose();
  });

  test('re-signing in as the same account after a rejected session recovers the kept request', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    api.placeScript
      ..add(() async => const OrderReply.lost())
      ..add(() async => problem(401, 'UNAUTHORIZED'))
      ..add(() async => reply(200, orderJson()));
    final orders = ordersFor(account, api);
    await account.signInAs('acc-1');
    await orders.submit(sampleDraft());
    await orders.recover();
    expect(orders.phase, OrderPhase.sessionEnded);
    expect(orders.order, isNull);
    await account.signInAs('acc-1');
    await Future<void>.delayed(Duration.zero);
    await Future<void>.delayed(Duration.zero);
    expect(orders.order?.id, 'order-1');
    expect(api.placed.map((b) => b['clientRequestKey']).toSet(), hasLength(1));
    orders.dispose();
  });

  test('a session rejected mid-poll hides the pickup code immediately', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    api.placeScript.add(() async => reply(201, orderJson()));
    final orders = ordersFor(account, api);
    await account.signInAs('acc-1');
    await orders.submit(sampleDraft());
    expect(orders.order!.pickupCode, isNotNull);
    api.placeScript.add(() async => const OrderReply.lost());
    api.placeScript.add(() async => problem(401, 'UNAUTHORIZED'));
    // A later unresolved request ends with a 401: private content is dropped.
    await orders.submit(sampleDraft(quantity: 1));
    await orders.recover();
    expect(orders.order, isNull);
    orders.dispose();
  });

  test('signing in again with nothing unresolved leaves no empty order screen', () async {
    final account = TestAccount();
    final api = FakeOrderSource();
    api.placeScript.add(() async => problem(401, 'UNAUTHORIZED'));
    final orders = ordersFor(account, api);
    await account.signInAs('acc-1');
    await orders.submit(sampleDraft());
    expect(orders.phase, OrderPhase.sessionEnded);
    expect(orders.showingOrder, isTrue, reason: 'the session-ended notice is shown');
    await account.signInAs('acc-1');
    await Future<void>.delayed(Duration.zero);
    expect(orders.phase, OrderPhase.none);
    expect(orders.showingOrder, isFalse, reason: 'nothing to show, so Food browses');
    orders.dispose();
  });
}
