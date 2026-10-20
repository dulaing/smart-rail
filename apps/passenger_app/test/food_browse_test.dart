import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/food/food_api.dart';
import 'package:passenger_app/food/food_controller.dart';
import 'package:passenger_app/food/food_models.dart';

import 'food_support.dart';

FoodController controllerFor(FakeFoodSource source) => FoodController(
  source: source,
  trains: trainsWith([
    journey('today-3001', 'SIM-3001', DateTime(2026, 10, 20)),
    journey(
      'today-1003',
      'SIM-1003',
      DateTime(2026, 10, 20),
      from: 'GPH',
      to: 'FOT',
    ),
    journey('tomorrow-3001', 'SIM-3001', DateTime(2026, 10, 21)),
    journey('yesterday-3001', 'SIM-3001', DateTime(2026, 10, 19)),
  ]),
);

void main() {
  group('formatLkr', () {
    test('formats integer minor LKR without floating point', () {
      expect(formatLkr(65000), 'LKR 650');
      expect(formatLkr(5), 'LKR 0.05');
      expect(formatLkr(0), 'LKR 0');
      expect(formatLkr(123456789), 'LKR 1,234,567.89');
      expect(formatLkr(100000), 'LKR 1,000');
    });
  });

  group('choosing a dated journey', () {
    test(
      'offers today and later dated journeys, not past ones, today first',
      () {
        final c = controllerFor(FakeFoodSource());
        final choices = c.choices();
        expect(choices.map((j) => j.id), [
          'today-1003',
          'today-3001',
          'tomorrow-3001',
        ]);
        expect(choices.map((j) => j.serviceDate.day), [20, 20, 21]);
        expect(c.isToday(DateTime(2026, 10, 20)), isTrue);
        expect(c.isToday(DateTime(2026, 10, 21)), isFalse);
        c.dispose();
      },
    );

    test('Colombo date decides today even when the phone clock is still on the previous UTC day', () {
      // 23:30 UTC on the 19th is already 05:00 on the 20th in Colombo.
      final c = FoodController(
        source: FakeFoodSource(),
        trains: trainsWith([
          journey('a', 'SIM-3001', DateTime(2026, 10, 19)),
          journey('b', 'SIM-3001', DateTime(2026, 10, 20)),
        ], now: DateTime.utc(2026, 10, 19, 23, 30)),
      );
      expect(c.choices().map((j) => j.id), ['b']);
      c.dispose();
    });

    test('reads the options of the selected dated journey and keeps the server reasons', () async {
      final source = fakeSource(
        options: (id) async => optionsFor(id, [
          option('plg', 'polgahawela', reason: null),
          option(
            'pdn',
            'peradeniya',
            orderable: false,
            reason: 'TRAIN_UNKNOWN',
          ),
          option('kdy', 'kandy', orderable: false, reason: 'STOP_PASSED'),
          option('gal', 'galle', orderable: false, reason: 'STOP_NOT_SERVED'),
        ]),
      );
      final c = controllerFor(source);
      await c.selectJourney(
        c.choices().firstWhere((j) => j.id == 'today-3001'),
      );
      expect(source.optionReads, ['today-3001']);
      expect(c.optionsLoad, FoodLoad.ready);
      expect(c.options!.options.map((o) => (o.orderable, o.reasonCode)), [
        (true, null),
        (false, 'TRAIN_UNKNOWN'),
        (false, 'STOP_PASSED'),
        (false, 'STOP_NOT_SERVED'),
      ]);
      c.dispose();
    });

    test('an option the server marks unavailable cannot be opened and never reads a menu', () async {
      final source = fakeSource(
        options: (id) async => optionsFor(id, [
          option(
            'pdn',
            'peradeniya',
            orderable: false,
            reason: 'JOURNEY_NOT_TODAY',
          ),
        ]),
      );
      final c = controllerFor(source);
      await c.selectJourney(c.choices().first);
      await c.selectOption(c.options!.options.single);
      expect(c.selectedOption, isNull);
      expect(source.menuReads, isEmpty);
      c.dispose();
    });

    test(
      'a slow answer for a previous journey cannot replace the new journey',
      () async {
        final slow = Completer<CollectionOptions>();
        final source = fakeSource(
          options: (id) => id == 'today-1003'
              ? slow.future
              : Future.value(optionsFor(id, [option('pdn', 'peradeniya')])),
        );
        final c = controllerFor(source);
        final first = c.selectJourney(
          c.choices().firstWhere((j) => j.id == 'today-1003'),
        );
        await Future<void>.delayed(Duration.zero);
        await c.selectJourney(
          c.choices().firstWhere((j) => j.id == 'today-3001'),
        );
        slow.complete(optionsFor('today-1003', [option('late', 'late-point')]));
        await first;
        expect(c.journey!.id, 'today-3001');
        expect(c.options!.options.single.stopId, 'pdn');
        c.dispose();
      },
    );

    test(
      'a provider outage is its own state and the train list is untouched',
      () async {
        final source = fakeSource(
          options: (_) => Future.error(
            const FoodReadException(
              FoodReadFailure.providerUnavailable,
              status: 503,
            ),
          ),
        );
        final c = controllerFor(source);
        await c.selectJourney(c.choices().first);
        expect(c.optionsLoad, FoodLoad.providerUnavailable);
        expect(c.options, isNull);
        expect(
          c.choices(),
          isNotEmpty,
          reason: 'public train choices do not depend on the food provider',
        );
        c.dispose();
      },
    );

    test(
      'an unknown journey and a connection failure are different states',
      () async {
        final source = FakeFoodSource()
          ..onOptions = (_) =>
              Future.error(const FoodReadException(FoodReadFailure.notFound));
        final c = controllerFor(source);
        await c.selectJourney(c.choices().first);
        expect(c.optionsLoad, FoodLoad.notFound);
        source.onOptions = (_) =>
            Future.error(const FoodReadException(FoodReadFailure.connection));
        await c.refreshOptions();
        expect(c.optionsLoad, FoodLoad.failed);
        c.dispose();
      },
    );

    test('a failed refresh keeps the last options and a recovered refresh clears the error', () async {
      var fail = false;
      final source = fakeSource(
        options: (id) => fail
            ? Future.error(const FoodReadException(FoodReadFailure.connection))
            : Future.value(optionsFor(id, [option('pdn', 'peradeniya')])),
      );
      final c = controllerFor(source);
      await c.selectJourney(c.choices().first);
      fail = true;
      await c.refreshOptions();
      expect(c.optionsLoad, FoodLoad.failed);
      expect(c.options!.options, hasLength(1));
      fail = false;
      await c.refreshOptions();
      expect(c.optionsLoad, FoodLoad.ready);
      c.dispose();
    });
  });

  group('menu and basket', () {
    Future<(FoodController, FakeFoodSource)> ready({FoodMenu? menu}) async {
      final source = fakeSource(
        options: (id) async => optionsFor(id, [option('pdn', 'peradeniya')]),
        menu: (point) async => menu ?? menuFor(point, [lamprais, tea]),
      );
      final c = controllerFor(source);
      await c.selectJourney(
        c.choices().firstWhere((j) => j.id == 'today-3001'),
      );
      await c.selectOption(c.options!.options.single);
      return (c, source);
    }

    test(
      'opens the menu of the chosen stop occurrence and totals integer prices',
      () async {
        final (c, source) = await ready();
        expect(source.menuReads, ['peradeniya']);
        expect(c.selectedOption!.stopId, 'pdn');
        c.setQuantity(lamprais, 2);
        c.setQuantity(tea, 1);
        expect(c.cartCount, 3);
        expect(c.totalMinorLkr, 85000 * 2 + 8000);
        expect(formatLkr(c.totalMinorLkr!), 'LKR 1,780');
        expect(c.cartLines.map((l) => (l.$1.id, l.$2)), [
          ('pdn-lamprais', 2),
          ('pdn-plain-tea', 1),
        ]);
        c.dispose();
      },
    );

    test('quantity is capped by stock and by the order limit, and zero removes the line', () async {
      final scarce = const FoodMenuItem(
        id: 'last',
        name: 'Last roll',
        priceMinorLkr: 12000,
        stockQuantity: 2,
      );
      final soldOut = const FoodMenuItem(
        id: 'gone',
        name: 'Gone',
        priceMinorLkr: 100,
        stockQuantity: 0,
      );
      final (c, _) = await ready(
        menu: menuFor('peradeniya', [scarce, soldOut, tea]),
      );
      c.setQuantity(scarce, 5);
      expect(c.quantityOf('last'), 2);
      c.setQuantity(soldOut, 1);
      expect(c.quantityOf('gone'), 0);
      c.setQuantity(tea, 99);
      expect(c.quantityOf('pdn-plain-tea'), FoodController.maxQuantity);
      c.setQuantity(scarce, 0);
      expect(c.cart.containsKey('last'), isFalse);
      c.dispose();
    });

    test(
      'a menu refresh drops removed items, caps reduced stock and says so',
      () async {
        final (c, source) = await ready();
        c.setQuantity(lamprais, 10);
        c.setQuantity(tea, 2);
        source.onMenu = (point) async => menuFor(point, [
          const FoodMenuItem(
            id: 'pdn-lamprais',
            name: 'Lamprais',
            priceMinorLkr: 90000,
            stockQuantity: 3,
          ),
        ]);
        await c.refreshMenu();
        expect(c.cart, {'pdn-lamprais': 3});
        expect(c.cartAdjusted, isTrue);
        expect(c.totalMinorLkr, 270000, reason: 'the new price applies');
        c.dispose();
      },
    );

    test('an empty menu is an empty state, not an error', () async {
      final (c, _) = await ready(menu: menuFor('peradeniya', const []));
      expect(c.menuLoad, FoodLoad.ready);
      expect(c.menu!.items, isEmpty);
      expect(c.canReview, isFalse);
      c.dispose();
    });

    test('a menu failure leaves the pickup options intact', () async {
      final source = fakeSource(
        options: (id) async => optionsFor(id, [option('pdn', 'peradeniya')]),
        menu: (_) => Future.error(
          const FoodReadException(FoodReadFailure.providerUnavailable),
        ),
      );
      final c = controllerFor(source);
      await c.selectJourney(c.choices().first);
      await c.selectOption(c.options!.options.single);
      expect(c.menuLoad, FoodLoad.providerUnavailable);
      expect(c.optionsLoad, FoodLoad.ready);
      expect(c.options!.options, hasLength(1));
      c.dispose();
    });

    test('changing the pickup or journey starts a clean basket', () async {
      final source = fakeSource(
        options: (id) async => optionsFor(id, [
          option('plg', 'polgahawela'),
          option('pdn', 'peradeniya'),
        ]),
        menu: (point) async => menuFor(point, [lamprais, tea]),
      );
      final c = controllerFor(source);
      await c.selectJourney(
        c.choices().firstWhere((j) => j.id == 'today-3001'),
      );
      await c.selectOption(c.options!.options.first);
      c.setQuantity(lamprais, 1);
      await c.selectOption(c.options!.options.last);
      expect(c.cartCount, 0);
      c.setQuantity(tea, 1);
      await c.selectJourney(
        c.choices().firstWhere((j) => j.id == 'tomorrow-3001'),
      );
      expect(c.cartCount, 0);
      expect(c.selectedOption, isNull);
      expect(c.menu, isNull);
      c.dispose();
    });

    test(
      'a slow menu for a previous pickup cannot overwrite the current one',
      () async {
        final slow = Completer<FoodMenu>();
        final source = fakeSource(
          options: (id) async => optionsFor(id, [
            option('plg', 'polgahawela'),
            option('pdn', 'peradeniya'),
          ]),
          menu: (point) => point == 'polgahawela'
              ? slow.future
              : Future.value(menuFor(point, [lamprais])),
        );
        final c = controllerFor(source);
        await c.selectJourney(c.choices().first);
        final first = c.selectOption(c.options!.options.first);
        await Future<void>.delayed(Duration.zero);
        await c.selectOption(c.options!.options.last);
        slow.complete(menuFor('polgahawela', [tea]));
        await first;
        expect(c.menu!.collectionPointId, 'peradeniya');
        c.dispose();
      },
    );

    test(
      'a pickup that becomes unavailable stays visible but cannot go to review',
      () async {
        final (c, source) = await ready();
        c.setQuantity(lamprais, 1);
        expect(c.canReview, isTrue);
        source.onOptions = (id) async => optionsFor(id, [
          option('pdn', 'peradeniya', orderable: false, reason: 'STOP_PASSED'),
        ]);
        await c.refreshOptions();
        expect(c.selectedOption, isNotNull);
        expect(c.selectedOption!.reasonCode, 'STOP_PASSED');
        expect(c.canReview, isFalse);
        c.dispose();
      },
    );

    test(
      'a pickup missing from a later answer is dropped with its basket',
      () async {
        final (c, source) = await ready();
        c.setQuantity(lamprais, 1);
        source.onOptions = (id) async =>
            optionsFor(id, [option('other', 'elsewhere')]);
        await c.refreshOptions();
        expect(c.selectedOption, isNull);
        expect(c.cartCount, 0);
        c.dispose();
      },
    );
  });
}
