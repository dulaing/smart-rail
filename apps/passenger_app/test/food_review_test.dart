import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/food/food_controller.dart';
import 'package:passenger_app/food/order_api.dart';
import 'package:passenger_app/food/orders_controller.dart';

import 'food_support.dart';
import 'order_support.dart';

void tallScreen(WidgetTester tester) {
  tester.view.physicalSize = const Size(400 * 2, 1400 * 2);
  tester.view.devicePixelRatio = 2;
  addTearDown(tester.view.reset);
}

/// A basket of two lamprais at a pickup the server calls orderable, ready to review.
Future<FoodController> basketReady() async {
  final source = fakeSource(
    options: (id) async => optionsFor(id, [option('pdn', 'peradeniya')]),
    menu: (point) async => menuFor(point, [lamprais, tea]),
  );
  final c = FoodController(
    source: source,
    trains: trainsWith([
      journey('today-3001', 'SIM-3001', DateTime(2026, 10, 20)),
    ]),
  );
  await c.selectJourney(c.choices().single);
  await c.selectOption(c.options!.options.single);
  c.setQuantity(lamprais, 2);
  return c;
}

Future<void> openReview(WidgetTester tester) async {
  await tester.tap(find.byKey(const Key('food-review')));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'review shows the stop, estimate, total, pay-at-kiosk and No-show policy',
    (tester) async {
      tallScreen(tester);
      final c = await basketReady();
      await tester.pumpWidget(foodApp(c));
      await openReview(tester);

      expect(find.text('Review your order'), findsOneWidget);
      expect(find.text('Collect at Point peradeniya'), findsOneWidget);
      expect(find.textContaining('Train expected 09:05'), findsOneWidget);
      expect(find.text('2 × Lamprais'), findsOneWidget);
      expect(find.byKey(const Key('food-review-total')), findsOneWidget);
      expect(find.text('LKR 1,700'), findsWidgets);
      expect(
        find.textContaining('Pay at the kiosk when you collect'),
        findsOneWidget,
      );
      expect(find.textContaining('No-show'), findsOneWidget);
      c.dispose();
    },
  );

  testWidgets(
    'a guest reviews publicly and is offered sign-in, not Place order',
    (tester) async {
      tallScreen(tester);
      final c = await basketReady();
      await tester.pumpWidget(foodApp(c));
      await openReview(tester);
      expect(find.byKey(const Key('food-sign-in')), findsOneWidget);
      expect(find.byKey(const Key('food-place-order')), findsNothing);
      c.dispose();
    },
  );

  testWidgets('cancelling sign-in keeps the basket and public browsing', (
    tester,
  ) async {
    tallScreen(tester);
    final c = await basketReady();
    final account = TestAccount(auth: FoodFakeAuth(signInFails: true));
    await tester.pumpWidget(foodApp(c, account: account));
    await openReview(tester);
    await tester.tap(find.byKey(const Key('food-sign-in')));
    await tester.pumpAndSettle();

    expect(
      find.text('Sign-in was cancelled. Your basket is still here.'),
      findsOneWidget,
    );
    expect(find.byKey(const Key('food-sign-in')), findsOneWidget);
    expect(c.cartCount, 2);
    await tester.tap(find.byKey(const Key('food-review-back')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('food-basket-summary')), findsOneWidget);
    c.dispose();
  });

  testWidgets('signing in from review keeps the route, menu and basket', (
    tester,
  ) async {
    tallScreen(tester);
    final c = await basketReady();
    final account = TestAccount();
    await tester.pumpWidget(foodApp(c, account: account));
    await openReview(tester);
    await tester.tap(find.byKey(const Key('food-sign-in')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('food-place-order')), findsOneWidget);
    expect(find.text('2 × Lamprais'), findsOneWidget);
    expect(c.selectedOption!.stopId, 'pdn');
    c.dispose();
  });

  testWidgets('an account that may not order is told so', (tester) async {
    tallScreen(tester);
    final c = await basketReady();
    final account = TestAccount();
    await tester.pumpWidget(foodApp(c, account: account));
    await tester.runAsync(
      () => account.signInAs('acc-1', permissions: ['saved-routes.manage']),
    );
    await openReview(tester);
    expect(find.text('Your account cannot place food orders.'), findsOneWidget);
    expect(find.byKey(const Key('food-place-order')), findsNothing);
    c.dispose();
  });

  testWidgets(
    'Place order sends one request and shows the confirmed order with its pickup code',
    (tester) async {
      tallScreen(tester);
      final c = await basketReady();
      final account = TestAccount();
      final api = FakeOrderSource()
        ..placeScript.add(() async => reply(201, orderJson()));
      final orders = ordersFor(account, api);
      await tester.pumpWidget(foodApp(c, account: account, orders: orders));
      await tester.runAsync(() => account.signInAs('acc-1'));
      await openReview(tester);

      await tester.tap(find.byKey(const Key('food-place-order')));
      await tester.pumpAndSettle();

      expect(api.placed, hasLength(1));
      expect(api.placed.single['collectionPointId'], 'peradeniya');
      expect(api.placed.single['pickupStopId'], 'pdn');
      expect(find.text('Order confirmed'), findsOneWidget);
      expect(find.byKey(const Key('food-pickup-code')), findsOneWidget);
      expect(find.text('482915'), findsOneWidget);
      expect(c.cartCount, 0, reason: 'the basket moved into the order');
      orders.dispose();
      c.dispose();
    },
  );

  testWidgets(
    'a 202 says Order outcome not yet confirmed and shows no pickup code',
    (tester) async {
      tallScreen(tester);
      final c = await basketReady();
      final account = TestAccount();
      final api = FakeOrderSource()
        ..placeScript.add(() async => reply(202, orderJson(status: 'PLACING')));
      final orders = ordersFor(account, api);
      await tester.pumpWidget(foodApp(c, account: account, orders: orders));
      await tester.runAsync(() => account.signInAs('acc-1'));
      await openReview(tester);
      await tester.tap(find.byKey(const Key('food-place-order')));
      await tester.pumpAndSettle();

      expect(find.text('Order outcome not yet confirmed'), findsOneWidget);
      expect(find.byKey(const Key('food-pickup-code')), findsNothing);
      expect(
        find.text('2 × Lamprais'),
        findsOneWidget,
        reason: 'the frozen order is still shown',
      );
      expect(find.byKey(const Key('food-check-again')), findsOneWidget);
      orders.dispose();
      c.dispose();
    },
  );

  testWidgets(
    'a refusal explains why in plain words and returns to the kept basket',
    (tester) async {
      tallScreen(tester);
      final c = await basketReady();
      final account = TestAccount();
      final api = FakeOrderSource()
        ..placeScript.add(() async => problem(422, 'CUTOFF_PASSED'));
      final orders = ordersFor(account, api);
      await tester.pumpWidget(foodApp(c, account: account, orders: orders));
      await tester.runAsync(() => account.signInAs('acc-1'));
      await openReview(tester);
      await tester.tap(find.byKey(const Key('food-place-order')));
      await tester.pumpAndSettle();

      expect(find.text('Order not placed'), findsOneWidget);
      expect(
        find.text('The order cutoff for this pickup has passed.'),
        findsOneWidget,
      );
      await tester.tap(find.byKey(const Key('food-refused-back')));
      await tester.pumpAndSettle();
      expect(find.text('Review your order'), findsOneWidget);
      expect(c.cartCount, 2);
      expect(orders.phase, OrderPhase.none);
      orders.dispose();
      c.dispose();
    },
  );

  testWidgets(
    'an unknown refusal code gets a generic message, never success or the raw code',
    (tester) async {
      tallScreen(tester);
      final c = await basketReady();
      final account = TestAccount();
      final api = FakeOrderSource()
        ..placeScript.add(() async => problem(422, 'BRAND_NEW_CODE'));
      final orders = ordersFor(account, api);
      await tester.pumpWidget(foodApp(c, account: account, orders: orders));
      await tester.runAsync(() => account.signInAs('acc-1'));
      await openReview(tester);
      await tester.tap(find.byKey(const Key('food-place-order')));
      await tester.pumpAndSettle();
      expect(
        find.text('Food cannot be ordered here right now.'),
        findsOneWidget,
      );
      expect(find.textContaining('BRAND_NEW_CODE'), findsNothing);
      expect(find.text('Order confirmed'), findsNothing);
      orders.dispose();
      c.dispose();
    },
  );

  testWidgets(
    'Check again on an unconfirmed order replays the same key and shows the confirmed code',
    (tester) async {
      tallScreen(tester);
      final c = await basketReady();
      final account = TestAccount();
      final api = FakeOrderSource()
        ..placeScript.add(() async => const OrderReply.lost())
        ..placeScript.add(() async => reply(200, orderJson()));
      final orders = ordersFor(account, api);
      await tester.pumpWidget(foodApp(c, account: account, orders: orders));
      await tester.runAsync(() => account.signInAs('acc-1'));
      await openReview(tester);
      await tester.tap(find.byKey(const Key('food-place-order')));
      await tester.pumpAndSettle();
      expect(find.text('Order outcome not yet confirmed'), findsOneWidget);

      await tester.tap(find.byKey(const Key('food-check-again')));
      await tester.pumpAndSettle();
      expect(api.placed, hasLength(2));
      expect(api.placed.last, api.placed.first);
      expect(find.text('Order confirmed'), findsOneWidget);
      expect(find.text('482915'), findsOneWidget);
      orders.dispose();
      c.dispose();
    },
  );

  testWidgets(
    'a key conflict is explained and offers only an explicit discard',
    (tester) async {
      tallScreen(tester);
      final c = await basketReady();
      final account = TestAccount();
      final api = FakeOrderSource()
        ..placeScript.add(
          () async => problem(409, 'CLIENT_REQUEST_KEY_IN_USE'),
        );
      final orders = ordersFor(account, api);
      await tester.pumpWidget(foodApp(c, account: account, orders: orders));
      await tester.runAsync(() => account.signInAs('acc-1'));
      await openReview(tester);
      await tester.tap(find.byKey(const Key('food-place-order')));
      await tester.pumpAndSettle();
      expect(
        find.textContaining('already used for a different order'),
        findsOneWidget,
      );
      expect(find.byKey(const Key('food-check-again')), findsNothing);
      await tester.tap(find.byKey(const Key('food-discard-request')));
      await tester.pumpAndSettle();
      expect(orders.phase, OrderPhase.none);
      orders.dispose();
      c.dispose();
    },
  );

  testWidgets('a rejected session hides the order and asks for sign-in again', (
    tester,
  ) async {
    tallScreen(tester);
    final c = await basketReady();
    final account = TestAccount();
    final api = FakeOrderSource()
      ..placeScript.add(() async => problem(401, 'UNAUTHORIZED'));
    final orders = ordersFor(account, api);
    await tester.pumpWidget(foodApp(c, account: account, orders: orders));
    await tester.runAsync(() => account.signInAs('acc-1'));
    await openReview(tester);
    await tester.tap(find.byKey(const Key('food-place-order')));
    await tester.pumpAndSettle();
    expect(find.textContaining('Your sign-in ended'), findsOneWidget);
    expect(find.byKey(const Key('food-pickup-code')), findsNothing);
    expect(find.byKey(const Key('food-sign-in-again')), findsOneWidget);
    orders.dispose();
    c.dispose();
  });
}
