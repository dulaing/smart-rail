import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/food/food_controller.dart';
import 'package:passenger_app/food/orders_controller.dart';

import 'food_support.dart';
import 'order_support.dart';

Future<FoodController> basketReady() async {
  final source = fakeSource(
    options: (id) async => optionsFor(id, [option('pdn', 'peradeniya')]),
    menu: (point) async => menuFor(point, [lamprais, tea]),
  );
  final c = FoodController(
    source: source,
    trains: trainsWith([journey('today-3001', 'SIM-3001', DateTime(2026, 10, 20))]),
  );
  await c.selectJourney(c.choices().single);
  await c.selectOption(c.options!.options.single);
  c.setQuantity(lamprais, 2);
  return c;
}

void tall(WidgetTester tester) {
  tester.view.physicalSize = const Size(400 * 2, 1800 * 2);
  tester.view.devicePixelRatio = 2;
  addTearDown(tester.view.reset);
}

Future<void> guidelines(WidgetTester tester) async {
  await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
  await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
  await expectLater(tester, meetsGuideline(textContrastGuideline));
}

/// Drives to one of the order screens and returns the pieces to dispose.
Future<void> reach(WidgetTester tester, String screen, FoodController c, TestAccount account, FakeOrderSource api) async {
  await tester.runAsync(() => account.signInAs('acc-1'));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const Key('food-review')));
  await tester.pumpAndSettle();
  if (screen == 'review') return;
  api.placeScript.add(
    () async => screen == 'pending' ? reply(202, orderJson(status: 'PLACING')) : reply(201, orderJson()),
  );
  await tester.tap(find.byKey(const Key('food-place-order')));
  await tester.pumpAndSettle();
}

void main() {
  for (final brightness in Brightness.values) {
    for (final screen in ['review', 'pending', 'confirmed']) {
      testWidgets('$screen screen meets tap target, label and contrast guidelines (${brightness.name})', (tester) async {
        tall(tester);
        final handle = tester.ensureSemantics();
        final c = await basketReady();
        final account = TestAccount();
        final api = FakeOrderSource();
        final orders = ordersFor(account, api);
        await tester.pumpWidget(foodApp(c, account: account, orders: orders, brightness: brightness));
        await reach(tester, screen, c, account, api);
        await guidelines(tester);
        handle.dispose();
        orders.dispose();
        c.dispose();
      });
    }
  }

  for (final lang in ['en', 'si', 'ta']) {
    for (final screen in ['review', 'pending', 'confirmed']) {
      testWidgets('$screen screen at 200% text has no overflow and keeps its action reachable ($lang)', (tester) async {
        tester.view.physicalSize = const Size(360 * 3, 640 * 3);
        tester.view.devicePixelRatio = 3;
        addTearDown(tester.view.reset);
        final c = await basketReady();
        final account = TestAccount();
        final api = FakeOrderSource();
        final orders = ordersFor(account, api);
        await tester.pumpWidget(
          MediaQuery(
            data: const MediaQueryData(textScaler: TextScaler.linear(2), size: Size(360, 640)),
            child: foodApp(c, account: account, orders: orders, locale: Locale(lang)),
          ),
        );
        await tester.runAsync(() => account.signInAs('acc-1'));
        await tester.pumpAndSettle();
        await tester.ensureVisible(find.byKey(const Key('food-review')));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('food-review')));
        await tester.pumpAndSettle();
        if (screen != 'review') {
          api.placeScript.add(
            () async => screen == 'pending' ? reply(202, orderJson(status: 'PLACING')) : reply(201, orderJson()),
          );
          await tester.scrollUntilVisible(find.byKey(const Key('food-place-order')), 200);
          await tester.ensureVisible(find.byKey(const Key('food-place-order')));
          await tester.pumpAndSettle();
          await tester.tap(find.byKey(const Key('food-place-order')));
          await tester.pumpAndSettle();
        }
        expect(tester.takeException(), isNull);
        final action = switch (screen) {
          'review' => const Key('food-place-order'),
          'pending' => const Key('food-check-again'),
          _ => const Key('food-order-done'),
        };
        await tester.scrollUntilVisible(find.byKey(action), 200);
        await tester.ensureVisible(find.byKey(action));
        await tester.pumpAndSettle();
        expect(tester.takeException(), isNull);
        final rect = tester.getRect(find.byKey(action));
        expect(rect.left, greaterThanOrEqualTo(0));
        expect(rect.right, lessThanOrEqualTo(360));
        expect(rect.height, greaterThanOrEqualTo(48), reason: 'target stays at least 48dp tall');
        orders.dispose();
        c.dispose();
      });
    }
  }

  testWidgets('screen readers hear the unconfirmed state as a live heading and the code digit by digit', (tester) async {
    tall(tester);
    final handle = tester.ensureSemantics();
    final c = await basketReady();
    final account = TestAccount();
    final api = FakeOrderSource();
    final orders = ordersFor(account, api);
    await tester.pumpWidget(foodApp(c, account: account, orders: orders));
    await reach(tester, 'pending', c, account, api);
    final title = tester.getSemantics(find.byKey(const Key('food-uncertain-title')));
    expect(title.label, 'Order outcome not yet confirmed');

    api.readScript.add(() async => reply(200, orderJson()));
    await tester.tap(find.byKey(const Key('food-check-again')));
    await tester.pumpAndSettle();
    expect(find.bySemanticsLabel(RegExp('Pickup code: 4 8 2 9 1 5')), findsOneWidget);
    expect(orders.phase, OrderPhase.none);
    handle.dispose();
    orders.dispose();
    c.dispose();
  });
}
