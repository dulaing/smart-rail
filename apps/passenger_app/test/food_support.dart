
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/food/food_api.dart';
import 'package:passenger_app/food/food_controller.dart';
import 'package:passenger_app/food/food_models.dart';
import 'package:passenger_app/food/food_pages.dart';
import 'package:passenger_app/l10n/app_localizations.dart';
import 'package:passenger_app/trains/train_api.dart';
import 'package:passenger_app/trains/train_cache.dart';
import 'package:passenger_app/trains/train_controller.dart';
import 'package:passenger_app/trains/train_models.dart';
import 'package:smartrail_core/smartrail_core.dart';

/// 2026-10-20 08:00 in Colombo, the presentation start used by the demo scenario.
final colomboMorning = DateTime.utc(2026, 10, 20, 2, 30);

class QuietTrainSource implements PassengerTrainSource {
  @override
  Future<List<PassengerStation>> stations() async => const [];
  @override
  Future<ApiRead<RouteSnapshot>> live(String origin, String destination, String? etag) =>
      Future.error(StateError('offline'));
  @override
  Future<String?> lastKnownStation(String journeyId) async => null;
  @override
  Future<List<PassengerJourney>> timetable(DateTime serviceDate) async => const [];
}

class QuietTrainCache implements PassengerTrainCache {
  @override
  Future<PublicTrainCache> read() async => const PublicTrainCache();
  @override
  Future<void> saveStations(List<PassengerStation> stations, DateTime at) async {}
  @override
  Future<void> saveTimetable(List<PassengerJourney> journeys, DateTime at) async {}
  @override
  Future<void> saveNotices(List<PassengerNotice> notices, DateTime at, String originId, String destinationId) async {}
}

PassengerJourney journey(String id, String train, DateTime date, {String from = 'FOT', String to = 'PDN'}) =>
    PassengerJourney(
      id: id,
      serviceDate: date,
      trainNumber: train,
      sourceLabel: 'Public timetable',
      stops: [
        PassengerStop(id: '$id-1', stationId: 's-$from', stationCode: from, sequence: 1),
        PassengerStop(id: '$id-2', stationId: 's-$to', stationCode: to, sequence: 2),
      ],
    );

/// A train controller holding a saved timetable, with the clock fixed to [colomboMorning].
TrainController trainsWith(List<PassengerJourney> journeys, {DateTime? now}) {
  final controller = TrainController(
    api: QuietTrainSource(),
    cache: QuietTrainCache(),
    clock: () => now ?? colomboMorning,
  );
  controller.saved = PublicTrainCache(journeys: journeys);
  return controller;
}

CollectionOption option(
  String stop,
  String point, {
  bool orderable = true,
  String? reason,
  DateTime? estimate,
  int cutoff = 30,
}) => CollectionOption(
  stopId: stop,
  stationId: 'station-$stop',
  pointId: point,
  pointName: 'Point $point',
  cutoffMinutes: cutoff,
  scheduledArrival: colomboMorning.add(const Duration(hours: 1)),
  estimatedArrival: estimate ?? colomboMorning.add(const Duration(hours: 1, minutes: 5)),
  orderBy: colomboMorning.add(const Duration(minutes: 35)),
  orderable: orderable,
  reasonCode: reason,
);

CollectionOptions optionsFor(String journeyId, List<CollectionOption> list) => CollectionOptions(
  journeyId: journeyId,
  trainNumber: 'SIM-3001',
  serviceDate: '2026-10-20',
  options: list,
);

FoodMenu menuFor(String point, List<FoodMenuItem> items) =>
    FoodMenu(collectionPointId: point, currency: 'LKR', items: items);

const lamprais = FoodMenuItem(id: 'pdn-lamprais', name: 'Lamprais', priceMinorLkr: 85000, stockQuantity: 25);
const tea = FoodMenuItem(id: 'pdn-plain-tea', name: 'Plain tea', priceMinorLkr: 8000, stockQuantity: 80);

class FakeFoodSource implements FoodSource {
  Future<CollectionOptions> Function(String journeyId) onOptions = (_) =>
      Future.error(const FoodReadException(FoodReadFailure.other));
  Future<FoodMenu> Function(String pointId) onMenu = (_) => Future.error(const FoodReadException(FoodReadFailure.other));
  final List<String> optionReads = [];
  final List<String> menuReads = [];

  @override
  Future<CollectionOptions> collectionOptions(String journeyId) {
    optionReads.add(journeyId);
    return onOptions(journeyId);
  }

  @override
  Future<FoodMenu> menu(String collectionPointId) {
    menuReads.add(collectionPointId);
    return onMenu(collectionPointId);
  }
}

FakeFoodSource fakeSource({
  Future<CollectionOptions> Function(String journeyId)? options,
  Future<FoodMenu> Function(String pointId)? menu,
}) {
  final source = FakeFoodSource();
  if (options != null) source.onOptions = options;
  if (menu != null) source.onMenu = menu;
  return source;
}

Widget foodApp(FoodController controller, {Brightness brightness = Brightness.light, Locale locale = const Locale('en')}) =>
    MaterialApp(
      theme: srTheme(SrProfile.passenger, brightness, locale: locale),
      locale: locale,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      supportedLocales: AppLocalizations.supportedLocales,
      home: Scaffold(body: FoodPage(controller: controller, active: true)),
    );

Matcher get providerDown => throwsA(isA<FoodReadException>());
