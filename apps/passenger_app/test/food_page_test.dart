import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/food/food_api.dart';
import 'package:passenger_app/food/food_controller.dart';
import 'package:passenger_app/food/food_models.dart';

import 'food_support.dart';

FoodController controllerWith(FakeFoodSource source) => FoodController(
  source: source,
  trains: trainsWith([
    journey('today-3001', 'SIM-3001', DateTime(2026, 10, 20)),
    journey('tomorrow-3001', 'SIM-3001', DateTime(2026, 10, 21)),
  ]),
);

/// Scrolls the page until [finder] is on screen; large text pushes controls below the fold.
Future<void> reveal(WidgetTester tester, Finder finder) async {
  await tester.scrollUntilVisible(finder, 100);
  await tester.ensureVisible(finder);
  await tester.pumpAndSettle();
}

Future<void> chooseTrain(WidgetTester tester, String id) async {
  await tester.scrollUntilVisible(find.byKey(const Key('food-choose-train')), 100);
  await tester.ensureVisible(find.byKey(const Key('food-choose-train')));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(const Key('food-choose-train')));
  await tester.pumpAndSettle();
  final sheetList = find.descendant(of: find.byType(DraggableScrollableSheet), matching: find.byType(Scrollable));
  await tester.scrollUntilVisible(find.byKey(Key('food-train-$id')), 100, scrollable: sheetList.first);
  await tester.ensureVisible(find.byKey(Key('food-train-$id')));
  await tester.pumpAndSettle();
  await tester.tap(find.byKey(Key('food-train-$id')));
  await tester.pumpAndSettle();
}

/// A tall phone, so every pickup card is built without scrolling.
void tallScreen(WidgetTester tester) {
  tester.view.physicalSize = const Size(400 * 2, 1400 * 2);
  tester.view.devicePixelRatio = 2;
  addTearDown(tester.view.reset);
}

void main() {
  final source = fakeSource(
    options: (id) async => optionsFor(id, [
      option('plg', 'polgahawela'),
      option('pdn', 'peradeniya', orderable: false, reason: 'TRAIN_UNKNOWN'),
      option('kdy', 'kandy', orderable: false, reason: 'STOP_PASSED'),
      option('gal', 'galle', orderable: false, reason: 'SOMETHING_NEW'),
    ]),
    menu: (point) async => menuFor(point, [
      lamprais,
      const FoodMenuItem(id: 'gone', name: 'Fish bun', priceMinorLkr: 10000, stockQuantity: 0),
    ]),
  );

  testWidgets('a guest browses without any sign-in prompt', (tester) async {
    tallScreen(tester);
    final c = controllerWith(source);
    await tester.pumpWidget(foodApp(c));
    expect(find.text('Choose a train'), findsWidgets);
    expect(find.textContaining('Sign in'), findsNothing);
    expect(find.text('Pick a dated train to see where you can collect food.'), findsOneWidget);
    c.dispose();
  });

  testWidgets('shows eligible pickups with a menu button and the server reasons for the rest', (tester) async {
    tallScreen(tester);
    final c = controllerWith(source);
    await tester.pumpWidget(foodApp(c));
    await chooseTrain(tester, 'today-3001');

    expect(find.text('SIM-3001 · FOT → PDN'), findsOneWidget);
    expect(find.textContaining('Today'), findsOneWidget);
    expect(find.text('Point polgahawela'), findsOneWidget);
    expect(find.byKey(const Key('food-option-plg')), findsOneWidget);
    expect(find.byKey(const Key('food-option-pdn')), findsNothing, reason: 'no order button when unavailable');
    expect(find.textContaining('no recent position for this train'), findsOneWidget);
    expect(find.text('The train has already passed this stop.'), findsOneWidget);
    expect(find.text('Food cannot be ordered here right now.'), findsOneWidget, reason: 'unknown code gets generic text');
    expect(find.textContaining('SOMETHING_NEW'), findsNothing);
    // The fixture instants are 03:35Z / 03:30Z / 03:05Z, shown in Colombo time (UTC+05:30).
    expect(find.text('Train due 09:05'), findsWidgets);
    expect(find.text('Scheduled 09:00'), findsWidgets);
    expect(find.text('Order by 08:35'), findsOneWidget, reason: 'only the orderable pickup shows an order-by time');
    c.dispose();
  });

  testWidgets('shows LKR prices, caps sold-out items and totals the basket', (tester) async {
    tallScreen(tester);
    final c = controllerWith(source);
    await tester.pumpWidget(foodApp(c));
    await chooseTrain(tester, 'today-3001');
    await tester.tap(find.byKey(const Key('food-option-plg')));
    await tester.pumpAndSettle();

    expect(find.text('Menu · Point polgahawela'), findsOneWidget);
    expect(find.text('LKR 850.00'), findsOneWidget);
    expect(find.text('Sold out'), findsOneWidget);
    expect(find.byKey(const Key('food-add-gone')), findsNothing);
    expect(find.byKey(const Key('food-basket-summary')), findsNothing);

    await tester.tap(find.byKey(const Key('food-add-pdn-lamprais')));
    await tester.tap(find.byKey(const Key('food-add-pdn-lamprais')));
    await tester.pumpAndSettle();
    expect(find.text('2 items · LKR 1,700.00'), findsOneWidget);

    await tester.tap(find.byKey(const Key('food-remove-pdn-lamprais')));
    await tester.pumpAndSettle();
    expect(find.text('1 item · LKR 850.00'), findsOneWidget);
    c.dispose();
  });

  testWidgets('a provider outage shows a calm message with retry, and recovers', (tester) async {
    tallScreen(tester);
    var down = true;
    final flaky = fakeSource(
      options: (id) => down
          ? Future.error(const FoodReadException(FoodReadFailure.providerUnavailable))
          : Future.value(optionsFor(id, [option('plg', 'polgahawela')])),
    );
    final c = controllerWith(flaky);
    await tester.pumpWidget(foodApp(c));
    await chooseTrain(tester, 'today-3001');
    expect(find.textContaining('Food ordering is unavailable right now'), findsOneWidget);
    expect(find.textContaining('Train information is not affected'), findsOneWidget);

    down = false;
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('food-option-plg')), findsOneWidget);
    c.dispose();
  });

  testWidgets('another service date is offered and explained by the server', (tester) async {
    tallScreen(tester);
    final dated = fakeSource(
      options: (id) async => optionsFor(id, [
        option('pdn', 'peradeniya', orderable: false, reason: id == 'tomorrow-3001' ? 'JOURNEY_NOT_TODAY' : null),
      ]),
    );
    final c = controllerWith(dated);
    await tester.pumpWidget(foodApp(c));
    await chooseTrain(tester, 'tomorrow-3001');
    expect(find.text('This train does not run today, so food cannot be ordered yet.'), findsOneWidget);
    c.dispose();
  });

  testWidgets('an empty menu and an empty options list are plain states', (tester) async {
    tallScreen(tester);
    final empty = fakeSource(
      options: (id) async => optionsFor(id, [option('plg', 'polgahawela')]),
      menu: (point) async => menuFor(point, const []),
    );
    final c = controllerWith(empty);
    await tester.pumpWidget(foodApp(c));
    await chooseTrain(tester, 'today-3001');
    await tester.tap(find.byKey(const Key('food-option-plg')));
    await tester.pumpAndSettle();
    expect(find.text('This collection point has no items available right now.'), findsOneWidget);
    c.dispose();
  });

  for (final brightness in Brightness.values) {
    testWidgets('meets tap target, label and contrast guidelines (${brightness.name})', (tester) async {
      tallScreen(tester);
      final handle = tester.ensureSemantics();
      final c = controllerWith(source);
      await tester.pumpWidget(foodApp(c, brightness: brightness));
      await chooseTrain(tester, 'today-3001');
      await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
      await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
      await expectLater(tester, meetsGuideline(textContrastGuideline));
      await tester.tap(find.byKey(const Key('food-option-plg')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('food-add-pdn-lamprais')));
      await tester.pumpAndSettle();
      await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
      await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
      await expectLater(tester, meetsGuideline(textContrastGuideline));
      handle.dispose();
      c.dispose();
    });
  }

  for (final lang in ['en', 'si', 'ta']) {
    testWidgets('200% text keeps the menu controls reachable without overflow ($lang)', (tester) async {
      tester.view.physicalSize = const Size(360 * 3, 640 * 3);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      final c = controllerWith(source);
      await tester.pumpWidget(
        MediaQuery(
          data: const MediaQueryData(textScaler: TextScaler.linear(2), size: Size(360, 640)),
          child: foodApp(c, locale: Locale(lang)),
        ),
      );
      await chooseTrain(tester, 'today-3001');
      expect(tester.takeException(), isNull);
      await reveal(tester, find.byKey(const Key('food-option-plg')));
      await tester.tap(find.byKey(const Key('food-option-plg')));
      await tester.pumpAndSettle();
      await reveal(tester, find.byKey(const Key('food-add-pdn-lamprais')));
      await tester.tap(find.byKey(const Key('food-add-pdn-lamprais')));
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.byKey(const Key('food-basket-summary')), findsOneWidget);
      c.dispose();
    });
  }

  testWidgets('item stepper announces its quantity to screen readers', (tester) async {
    tallScreen(tester);
    final handle = tester.ensureSemantics();
    final c = controllerWith(source);
    await tester.pumpWidget(foodApp(c));
    await chooseTrain(tester, 'today-3001');
    await tester.tap(find.byKey(const Key('food-option-plg')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('food-add-pdn-lamprais')));
    await tester.pumpAndSettle();
    expect(find.bySemanticsLabel(RegExp('Lamprais: 1 in basket')), findsOneWidget);
    expect(tester.getSemantics(find.byKey(const Key('food-add-pdn-lamprais'))).tooltip, 'Add one Lamprais');
    expect(tester.getSemantics(find.byKey(const Key('food-remove-pdn-lamprais'))).tooltip, 'Remove one Lamprais');
    handle.dispose();
    c.dispose();
  });
}
