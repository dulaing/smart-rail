import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/food/food_controller.dart';
import 'package:passenger_app/food/order_api.dart';
import 'package:passenger_app/food/orders_controller.dart';

import 'food_support.dart';
import 'order_support.dart';

Map<String, dynamic> listBody(List<Map<String, dynamic>> orders) => {'orders': orders};

Future<(TestAccount, FakeOrderSource, FoodOrdersController)> confirmedOrder({
  Map<String, dynamic>? order,
}) async {
  final account = TestAccount();
  final api = FakeOrderSource();
  api.listScript.add(() async => reply(200, listBody(const [])));
  final orders = ordersFor(account, api);
  await account.signInAs('acc-1');
  api.placeScript.add(() async => reply(201, order ?? orderJson()));
  await orders.submit(sampleDraft());
  expect(orders.order, isNotNull);
  return (account, api, orders);
}

void main() {
  group('tracking', () {
    test('a delay moves the pickup time and its evidence age with the same order and code', () async {
      final (_, api, orders) = await confirmedOrder();
      api.listScript.add(
        () async => reply(
          200,
          listBody([
            orderJson(
              sequence: 2,
              estimate: DateTime.utc(2026, 10, 20, 3, 50),
              evidence: DateTime.utc(2026, 10, 20, 3, 40),
              updatedAt: DateTime.utc(2026, 10, 20, 3, 41),
            ),
          ]),
        ),
      );
      await orders.refreshOrders();
      expect(orders.order!.pickupTime!.estimatedArrival, DateTime.utc(2026, 10, 20, 3, 50));
      expect(orders.order!.pickupTime!.etaEvidenceAt, DateTime.utc(2026, 10, 20, 3, 40));
      expect(orders.order!.pickupCode, '482915');
      orders.dispose();
    });

    test('callback-driven Ready, then Collected, arrive through polling', () async {
      final (_, api, orders) = await confirmedOrder();
      api.listScript.add(() async => reply(200, listBody([orderJson(status: 'READY', sequence: 2)])));
      await orders.refreshOrders();
      expect(orders.order!.status, 'READY');
      api.listScript.add(() async => reply(200, listBody([orderJson(status: 'COLLECTED', sequence: 3)])));
      await orders.refreshOrders();
      expect(orders.order!.status, 'COLLECTED');
      orders.dispose();
    });

    test('a late older response cannot regress Ready, Collected or Cancelled', () async {
      final (_, api, orders) = await confirmedOrder();
      final slow = Completer<OrderReply>();
      api.listScript.add(() => slow.future); // issued first, answered last
      final late = orders.refreshOrders();
      await Future<void>.delayed(Duration.zero);
      // A second path (cancel's reread) already learned the newer state.
      api.readScript.add(() async => reply(200, orderJson(status: 'READY', sequence: 3)));
      api.cancelScript.add(() async => problem(409, 'CUTOFF_PASSED'));
      await orders.cancel();
      expect(orders.order!.status, 'READY');
      slow.complete(reply(200, listBody([orderJson(status: 'ACCEPTED', sequence: 1)])));
      await late;
      expect(orders.order!.status, 'READY', reason: 'the older ACCEPTED answer is ignored');
      expect(orders.order!.statusSequence, 3);
      orders.dispose();
    });

    test('a terminal order is never replaced or revived, even by a newer sequence', () async {
      final (_, api, orders) = await confirmedOrder();
      api.listScript.add(
        () async => reply(200, listBody([orderJson(status: 'CANCELLED', sequence: 4, reasonCode: 'TRAIN_ENDED_EARLY')])),
      );
      await orders.refreshOrders();
      expect(orders.order!.status, 'CANCELLED');
      // Withdrawal of the early ending reopens eligibility but must not revive this order.
      api.listScript.add(() async => reply(200, listBody([orderJson(status: 'ACCEPTED', sequence: 9)])));
      await orders.refreshOrders();
      expect(orders.order!.status, 'CANCELLED');
      expect(orders.order!.reasonCode, 'TRAIN_ENDED_EARLY');
      orders.dispose();
    });

    test('Collected and No-show stay as history in the list', () async {
      final (_, api, orders) = await confirmedOrder();
      api.listScript.add(
        () async => reply(
          200,
          listBody([
            orderJson(status: 'COLLECTED', sequence: 3),
            orderJson(id: 'order-2', status: 'NO_SHOW', sequence: 3),
          ]),
        ),
      );
      await orders.refreshOrders();
      expect(orders.recentOrders.map((o) => o.status).toSet(), {'COLLECTED', 'NO_SHOW'});
      orders.dispose();
    });

    test('PLACING entries in the list never appear as confirmed orders', () async {
      final account = TestAccount();
      final api = FakeOrderSource();
      api.listScript.add(() async => reply(200, listBody([orderJson(status: 'PLACING', pickupCode: null)])));
      final orders = ordersFor(account, api);
      await account.signInAs('acc-1');
      expect(orders.recentOrders, isEmpty);
      orders.dispose();
    });

    test('polling while visible refreshes open orders and stops when everything is terminal', () async {
      final (_, api, orders) = await confirmedOrder();
      final before = api.lists;
      api.listScript.add(() async => reply(200, listBody([orderJson(status: 'COLLECTED', sequence: 3)])));
      await orders.tick();
      expect(api.lists, before + 1);
      await orders.tick();
      expect(api.lists, before + 1, reason: 'no open order left to watch');
      orders.dispose();
    });

    test('a 401 on a refresh hides every order and pickup code at once', () async {
      final (_, api, orders) = await confirmedOrder();
      api.listScript.add(() async => problem(401, 'UNAUTHORIZED'));
      await orders.refreshOrders();
      expect(orders.order, isNull);
      expect(orders.recentOrders, isEmpty);
      expect(orders.phase, OrderPhase.sessionEnded);
      orders.dispose();
    });
  });

  group('cancellation', () {
    test('sends one cancellation after rereading, and shows the cancelled order with its reason', () async {
      final (_, api, orders) = await confirmedOrder();
      api.readScript.add(() async => reply(200, orderJson()));
      api.cancelScript.add(
        () async => reply(200, orderJson(status: 'CANCELLED', sequence: 2, reasonCode: 'PASSENGER_CANCELLED')),
      );
      await orders.cancel();
      expect(api.reads, ['order-1']);
      expect(api.cancels, ['order-1']);
      expect(orders.order!.status, 'CANCELLED');
      expect(orders.order!.reasonCode, 'PASSENGER_CANCELLED');
      expect(orders.cancelProblem, isNull);
      orders.dispose();
    });

    test('repeated taps send a single cancellation', () async {
      final (_, api, orders) = await confirmedOrder();
      final gate = Completer<OrderReply>();
      api.readScript.add(() async => reply(200, orderJson()));
      api.cancelScript.add(() => gate.future);
      final first = orders.cancel();
      await orders.cancel();
      await orders.cancel();
      await Future<void>.delayed(Duration.zero);
      gate.complete(reply(200, orderJson(status: 'CANCELLED', sequence: 2, reasonCode: 'PASSENGER_CANCELLED')));
      await first;
      expect(api.cancels, hasLength(1));
      orders.dispose();
    });

    test('it does not send a cancellation when the reread shows the order is already final', () async {
      final (_, api, orders) = await confirmedOrder();
      api.readScript.add(() async => reply(200, orderJson(status: 'COLLECTED', sequence: 3)));
      await orders.cancel();
      expect(api.cancels, isEmpty);
      expect(orders.order!.status, 'COLLECTED');
      orders.dispose();
    });

    test('a 409 from the server is shown with its reason and the order stays', () async {
      final (_, api, orders) = await confirmedOrder();
      api.readScript.add(() async => reply(200, orderJson()));
      api.cancelScript.add(() async => problem(409, 'CUTOFF_PASSED'));
      await orders.cancel();
      expect(orders.cancelProblem, 'CUTOFF_PASSED');
      expect(orders.order!.status, 'ACCEPTED');
      expect(orders.cancelling, isFalse);
      orders.dispose();
    });

    test('a 503 says nothing changed and allows another try', () async {
      final (_, api, orders) = await confirmedOrder();
      api.readScript.add(() async => reply(200, orderJson()));
      api.cancelScript.add(() async => problem(503, 'PROVIDER_UNAVAILABLE'));
      await orders.cancel();
      expect(orders.cancelProblem, 'PROVIDER_UNAVAILABLE');
      api.readScript.add(() async => reply(200, orderJson()));
      api.cancelScript.add(
        () async => reply(200, orderJson(status: 'CANCELLED', sequence: 2, reasonCode: 'PASSENGER_CANCELLED')),
      );
      await orders.cancel();
      expect(orders.order!.status, 'CANCELLED');
      expect(orders.cancelProblem, isNull);
      orders.dispose();
    });

    test('a lost reply is reconciled by rereading: it did go through', () async {
      final (_, api, orders) = await confirmedOrder();
      api.readScript
        ..add(() async => reply(200, orderJson()))
        ..add(() async => reply(200, orderJson(status: 'CANCELLED', sequence: 2, reasonCode: 'PASSENGER_CANCELLED')));
      api.cancelScript.add(() async => const OrderReply.lost());
      await orders.cancel();
      expect(api.cancels, hasLength(1), reason: 'no blind second cancellation');
      expect(orders.order!.status, 'CANCELLED');
      expect(orders.cancelProblem, isNull);
      orders.dispose();
    });

    test('a lost reply the reread cannot settle is reported as unconfirmed, never as success', () async {
      final (_, api, orders) = await confirmedOrder();
      api.readScript
        ..add(() async => reply(200, orderJson()))
        ..add(() async => reply(200, orderJson()));
      api.cancelScript.add(() async => const OrderReply.lost());
      await orders.cancel();
      expect(orders.cancelProblem, 'UNCONFIRMED');
      expect(orders.order!.status, 'ACCEPTED');
      orders.dispose();
    });

    test('an early ending that cancels the order arrives with its reason', () async {
      final (_, api, orders) = await confirmedOrder();
      api.listScript.add(
        () async => reply(200, listBody([orderJson(status: 'CANCELLED', sequence: 2, reasonCode: 'TRAIN_ENDED_EARLY')])),
      );
      await orders.refreshOrders();
      expect(orders.order!.status, 'CANCELLED');
      expect(orders.order!.reasonCode, 'TRAIN_ENDED_EARLY');
      orders.dispose();
    });
  });

  test('a provider cutoff change from 30 to 45 reaches the pickup list without an app release', () async {
    var cutoff = 30;
    var orderBy = colomboMorning.add(const Duration(minutes: 35));
    final source = fakeSource(
      options: (id) async => optionsFor(id, [
        option('pdn', 'peradeniya', cutoff: cutoff, orderBy: orderBy),
      ]),
    );
    final c = FoodController(
      source: source,
      trains: trainsWith([journey('today-3001', 'SIM-3001', DateTime(2026, 10, 20))]),
    );
    await c.selectJourney(c.choices().single);
    expect(c.options!.options.single.cutoffMinutes, 30);
    cutoff = 45;
    orderBy = colomboMorning.add(const Duration(minutes: 20));
    await c.refreshOptions();
    expect(c.options!.options.single.cutoffMinutes, 45);
    expect(c.options!.options.single.orderBy, colomboMorning.add(const Duration(minutes: 20)));
    c.dispose();
  });

  group('screens', () {
    Future<void> tall(WidgetTester tester) async {
      tester.view.physicalSize = const Size(400 * 2, 1600 * 2);
      tester.view.devicePixelRatio = 2;
      addTearDown(tester.view.reset);
    }

    Future<(FoodController, TestAccount, FakeOrderSource, FoodOrdersController)> ready(
      WidgetTester tester, {
      Map<String, dynamic>? order,
    }) async {
      final source = fakeSource(
        options: (id) async => optionsFor(id, [option('pdn', 'peradeniya')]),
        menu: (point) async => menuFor(point, [lamprais]),
      );
      final c = FoodController(
        source: source,
        trains: trainsWith([journey('today-3001', 'SIM-3001', DateTime(2026, 10, 20))]),
      );
      final account = TestAccount();
      final api = FakeOrderSource();
      api.listScript.add(() async => reply(200, listBody([order ?? orderJson()])));
      final orders = ordersFor(account, api);
      await tester.pumpWidget(foodApp(c, account: account, orders: orders));
      await tester.runAsync(() => account.signInAs('acc-1'));
      await tester.pumpAndSettle();
      return (c, account, api, orders);
    }

    testWidgets('a recent order opens with its pickup code, estimate and evidence age', (tester) async {
      await tall(tester);
      final (c, _, _, orders) = await ready(tester);
      await tester.tap(find.byKey(const Key('food-recent-order-1')));
      await tester.pumpAndSettle();
      expect(find.text('Accepted'), findsOneWidget);
      expect(find.text('482915'), findsOneWidget);
      expect(find.text('Train due 09:05'), findsOneWidget);
      expect(find.textContaining('Estimate from train evidence at 08:50'), findsOneWidget);
      orders.dispose();
      c.dispose();
    });

    testWidgets('open orders sit above the pickups and finished ones below them', (tester) async {
      await tall(tester);
      final source = fakeSource(
        options: (id) async => optionsFor(id, [option('pdn', 'peradeniya')]),
        menu: (point) async => menuFor(point, [lamprais]),
      );
      final c = FoodController(
        source: source,
        trains: trainsWith([journey('today-3001', 'SIM-3001', DateTime(2026, 10, 20))]),
      );
      await c.selectJourney(c.choices().single);
      final account = TestAccount();
      final api = FakeOrderSource();
      api.listScript.add(
        () async => reply(
          200,
          listBody([
            orderJson(id: 'open-1'),
            orderJson(id: 'done-1', status: 'COLLECTED', sequence: 3),
          ]),
        ),
      );
      final orders = ordersFor(account, api);
      await tester.pumpWidget(foodApp(c, account: account, orders: orders));
      await tester.runAsync(() => account.signInAs('acc-1'));
      await tester.pumpAndSettle();

      final openY = tester.getTopLeft(find.byKey(const Key('food-recent-open-1'))).dy;
      final pickupY = tester.getTopLeft(find.text('Where to collect')).dy;
      final historyY = tester.getTopLeft(find.text('Order history')).dy;
      expect(openY, lessThan(pickupY), reason: 'an open order is found before browsing');
      expect(historyY, greaterThan(pickupY), reason: 'finished orders do not push the pickups down');
      expect(find.byKey(const Key('food-recent-done-1')), findsOneWidget);
      orders.dispose();
      c.dispose();
    });

    testWidgets('a collected or cancelled order shows no pickup code', (tester) async {
      await tall(tester);
      final (c, _, _, orders) = await ready(tester, order: orderJson(status: 'COLLECTED', sequence: 3));
      await tester.tap(find.byKey(const Key('food-recent-order-1')));
      await tester.pumpAndSettle();
      expect(find.text('Collected'), findsWidgets);
      expect(find.byKey(const Key('food-pickup-code')), findsNothing);
      expect(find.byKey(const Key('food-cancel-order')), findsNothing);
      orders.dispose();
      c.dispose();
    });

    testWidgets('cancelling asks first, then shows the server\'s refusal reason honestly', (tester) async {
      await tall(tester);
      final (c, _, api, orders) = await ready(tester);
      api.readScript.add(() async => reply(200, orderJson()));
      api.cancelScript.add(() async => problem(409, 'CUTOFF_PASSED'));
      await tester.tap(find.byKey(const Key('food-recent-order-1')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('food-cancel-order')));
      await tester.pumpAndSettle();
      expect(find.text('Cancel this order?'), findsOneWidget);
      await tester.tap(find.byKey(const Key('food-keep-order')));
      await tester.pumpAndSettle();
      expect(api.cancels, isEmpty, reason: 'keeping the order sends nothing');

      await tester.tap(find.byKey(const Key('food-cancel-order')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('food-confirm-cancel')));
      await tester.pumpAndSettle();
      expect(api.cancels, hasLength(1));
      expect(find.text('The order cutoff for this pickup has passed.'), findsOneWidget);
      expect(find.text('Accepted'), findsOneWidget);
      orders.dispose();
      c.dispose();
    });

    testWidgets('a successful cancellation shows Cancelled with the passenger reason', (tester) async {
      await tall(tester);
      final (c, _, api, orders) = await ready(tester);
      api.readScript.add(() async => reply(200, orderJson()));
      api.cancelScript.add(
        () async => reply(200, orderJson(status: 'CANCELLED', sequence: 2, reasonCode: 'PASSENGER_CANCELLED')),
      );
      await tester.tap(find.byKey(const Key('food-recent-order-1')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('food-cancel-order')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('food-confirm-cancel')));
      await tester.pumpAndSettle();
      expect(find.text('Cancelled'), findsOneWidget);
      expect(find.text('You cancelled this order.'), findsOneWidget);
      expect(find.byKey(const Key('food-pickup-code')), findsNothing);
      orders.dispose();
      c.dispose();
    });

    testWidgets('a train early ending explains the cancellation in plain words', (tester) async {
      await tall(tester);
      final (c, _, _, orders) = await ready(
        tester,
        order: orderJson(status: 'CANCELLED', sequence: 4, reasonCode: 'TRAIN_ENDED_EARLY'),
      );
      await tester.tap(find.byKey(const Key('food-recent-order-1')));
      await tester.pumpAndSettle();
      expect(find.text('The train ends before this stop, so this order was cancelled.'), findsOneWidget);
      orders.dispose();
      c.dispose();
    });
  });
}
