import 'dart:async';

import 'package:flutter_test/flutter_test.dart';
import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';
import 'package:passenger_app/l10n/app_localizations.dart';
import 'package:passenger_app/trains/train_api.dart';
import 'package:passenger_app/trains/train_cache.dart';
import 'package:passenger_app/trains/train_controller.dart';
import 'package:passenger_app/trains/train_models.dart';
import 'package:passenger_app/trains/train_pages.dart';
import 'package:shared_preferences/shared_preferences.dart';

class MemoryTrainCache implements PassengerTrainCache {
  PublicTrainCache value;
  MemoryTrainCache([this.value = const PublicTrainCache()]);
  @override
  Future<PublicTrainCache> read() async => value;
  @override
  Future<void> saveStations(
    List<PassengerStation> stations,
    DateTime at,
  ) async => value = PublicTrainCache(
    stations: stations,
    journeys: value.journeys,
    notices: value.notices,
    stationsDownloadedAt: at,
    timetableDownloadedAt: value.timetableDownloadedAt,
    noticesDownloadedAt: value.noticesDownloadedAt,
  );
  @override
  Future<void> saveTimetable(
    List<PassengerJourney> journeys,
    DateTime at,
  ) async {
    final byId = {
      for (final j in value.journeys) j.id: j,
      for (final j in journeys) j.id: j,
    };
    value = PublicTrainCache(
      stations: value.stations,
      journeys: byId.values.toList(),
      notices: value.notices,
      timetableDownloadedAt: at,
      stationsDownloadedAt: value.stationsDownloadedAt,
      noticesDownloadedAt: value.noticesDownloadedAt,
    );
  }

  @override
  Future<void> saveNotices(
    List<PassengerNotice> notices,
    DateTime at,
    String originId,
    String destinationId,
  ) async => value = PublicTrainCache(
    stations: value.stations,
    journeys: value.journeys,
    notices: notices,
    timetableDownloadedAt: value.timetableDownloadedAt,
    stationsDownloadedAt: value.stationsDownloadedAt,
    noticesDownloadedAt: at,
  );
}

class FakeTrainSource implements PassengerTrainSource {
  final List<PassengerStation> stationList = const [
    PassengerStation(
      id: 'gph',
      code: 'GPH',
      nameEn: 'Gampaha',
      nameSi: 'ගම්පහ',
      nameTa: 'கம்பஹா',
    ),
    PassengerStation(
      id: 'fot',
      code: 'FOT',
      nameEn: 'Colombo Fort',
      nameSi: 'කොළඹ කොටුව',
      nameTa: 'கொழும்பு கோட்டை',
    ),
    PassengerStation(
      id: 'rgm',
      code: 'RGM',
      nameEn: 'Ragama',
      nameSi: 'රාගම',
      nameTa: 'ராகம',
    ),
  ];
  final List<Completer<ApiRead<RouteSnapshot>>> pending = [];
  bool failLive = false;
  String? lastKnownStationId = 'rgm';
  List<PassengerJourney> timetableRows = const [];
  final List<DateTime> requestedDates = [];
  DateTime server = DateTime.utc(2026, 10, 18, 4);
  PassengerLiveTrain train(String id) => PassengerLiveTrain(
    journeyId: id,
    serviceDate: DateTime.utc(2026, 10, 18),
    trainNumber: id,
    originStopId: 'gph-stop',
    destinationStopId: 'fot-stop',
    scheduledDeparture: server.add(const Duration(minutes: 2)),
    scheduledArrival: server.add(const Duration(minutes: 30)),
    estimatedDeparture: server.add(const Duration(minutes: 3)),
    estimatedArrival: server.add(const Duration(minutes: 31)),
    freshness: 'CURRENT',
    overdue: false,
    availability: 'RUNNING',
    lastObservedAt: server,
  );
  RouteSnapshot snapshot(String origin, String id) => RouteSnapshot(
    originId: origin,
    destinationId: 'fot',
    trains: [train(id)],
    notices: [],
    observedAt: server,
    downloadedAt: server,
  );
  @override
  Future<List<PassengerStation>> stations() async => stationList;
  @override
  Future<String?> lastKnownStation(String journeyId) async =>
      lastKnownStationId;
  @override
  Future<ApiRead<RouteSnapshot>> live(
    String origin,
    String destination,
    String? etag,
  ) {
    if (failLive) return Future.error(StateError('offline'));
    final completer = Completer<ApiRead<RouteSnapshot>>();
    pending.add(completer);
    return completer.future;
  }

  void respond(int index, String origin, String id) =>
      pending[index].complete(ApiRead(snapshot(origin, id), '"$id"', server));
  @override
  Future<List<PassengerJourney>> timetable(DateTime serviceDate) async {
    requestedDates.add(serviceDate);
    return timetableRows.where((j) => j.serviceDate == serviceDate).toList();
  }
}

void main() {
  test(
    'a superseded route response is ignored while the new route loads',
    () async {
      final api = FakeTrainSource();
      final c = TrainController(
        api: api,
        cache: MemoryTrainCache(),
        clock: () => api.server,
      );
      c.originId = 'gph';
      c.destinationId = 'fot';
      final oldRequest = c.refresh();
      await Future<void>.delayed(Duration.zero);
      final newRoute = c.setRoute('rgm', 'fot');
      await Future<void>.delayed(Duration.zero);
      api.respond(1, 'rgm', 'new');
      await newRoute;
      api.respond(0, 'gph', 'old');
      await oldRequest;
      expect(c.originId, 'rgm');
      expect(c.liveTrains.single.trainNumber, 'new');
      c.dispose();
    },
  );

  test('304 and device time passage do not reset observation age', () async {
    final api = FakeTrainSource();
    var device = api.server;
    final c = TrainController(
      api: api,
      cache: MemoryTrainCache(),
      clock: () => device,
    );
    c.originId = 'gph';
    c.destinationId = 'fot';
    final request = c.refresh();
    await Future<void>.delayed(Duration.zero);
    api.respond(0, 'gph', 'one');
    await request;
    expect(c.freshnessFor(c.liveTrains.single), 'CURRENT');
    device = device.add(const Duration(seconds: 180));
    c.notifyListeners();
    expect(c.freshnessFor(c.liveTrains.single), 'AGEING');
    final notModified = Completer<ApiRead<RouteSnapshot>>();
    api.pending.add(notModified);
    final poll = c.refresh();
    await Future<void>.delayed(Duration.zero);
    api.pending.last.complete(ApiRead(null, '"one"', device));
    await poll;
    device = device.add(const Duration(seconds: 60));
    c.notifyListeners();
    expect(c.freshnessFor(c.liveTrains.single), 'UNKNOWN');
    expect(
      c.liveTrains.single.estimatedArrival,
      isNotNull,
    ); // UI hides this at UNKNOWN.
    c.dispose();
  });

  test('polling pauses in background and refreshes after resume', () async {
    final api = FakeTrainSource();
    final c =
        TrainController(
            api: api,
            cache: MemoryTrainCache(),
            clock: () => api.server,
          )
          ..originId = 'gph'
          ..destinationId = 'fot'
          ..paused = true;
    await c.refresh();
    expect(api.pending, isEmpty);
    c.paused = false;
    final resumed = c.refresh();
    await Future<void>.delayed(Duration.zero);
    expect(api.pending, hasLength(1));
    api.respond(0, 'gph', 'resumed');
    await resumed;
    expect(c.liveTrains.single.trainNumber, 'resumed');
    c.dispose();
  });

  testWidgets(
    'the route card hides an unknown ETA and detail draws every stop',
    (tester) async {
      final api = FakeTrainSource();
      var device = api.server;
      final journey = PassengerJourney(
        id: 'one',
        serviceDate: DateTime.utc(2026, 10, 18),
        trainNumber: 'SIM-1003',
        sourceLabel: 'Fictional demo timetable',
        stops: [
          PassengerStop(
            id: 'gph-stop',
            stationId: 'gph',
            stationCode: 'GPH',
            sequence: 1,
            departure: device.add(const Duration(minutes: 2)),
          ),
          PassengerStop(
            id: 'rgm-stop',
            stationId: 'rgm',
            stationCode: 'RGM',
            sequence: 2,
            arrival: device.add(const Duration(minutes: 10)),
            departure: device.add(const Duration(minutes: 11)),
          ),
          PassengerStop(
            id: 'fot-stop',
            stationId: 'fot',
            stationCode: 'FOT',
            sequence: 3,
            arrival: device.add(const Duration(minutes: 30)),
          ),
        ],
      );
      api.timetableRows = [journey];
      final c =
          TrainController(
              api: api,
              cache: MemoryTrainCache(),
              clock: () => device,
            )
            ..stations = api.stationList
            ..originId = 'gph'
            ..destinationId = 'fot'
            ..liveTrains = [api.train('one')];
      await tester.pumpWidget(
        MaterialApp(
          theme: srTheme(SrProfile.passenger, Brightness.light),
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          home: Scaffold(body: TrainsPage(controller: c)),
        ),
      );
      expect(find.textContaining('Expected arrival'), findsOneWidget);
      device = device.add(const Duration(seconds: 240));
      c.notifyListeners();
      await tester.pump();
      expect(find.textContaining('Expected arrival'), findsNothing);
      expect(find.textContaining('Scheduled arrival'), findsOneWidget);
      await tester.tap(find.byKey(const Key('train-one')));
      await tester.pumpAndSettle();
      expect(find.text('Ragama'), findsOneWidget);
      expect(find.text('Colombo Fort'), findsOneWidget);
      c.dispose();
    },
  );

  testWidgets(
    'confirmed platform times and early-ending notices remain explicit',
    (tester) async {
      final api = FakeTrainSource();
      final journey = PassengerJourney(
        id: 'one',
        serviceDate: DateTime.utc(2026, 10, 18),
        trainNumber: 'SIM-1003',
        sourceLabel: 'Fictional demo timetable',
        stops: [
          PassengerStop(
            id: 'gph-stop',
            stationId: 'gph',
            stationCode: 'GPH',
            sequence: 1,
            departure: api.server.add(const Duration(minutes: 2)),
          ),
          PassengerStop(
            id: 'rgm-stop',
            stationId: 'rgm',
            stationCode: 'RGM',
            sequence: 2,
            arrival: api.server.add(const Duration(minutes: 10)),
            departure: api.server.add(const Duration(minutes: 11)),
          ),
          PassengerStop(
            id: 'fot-stop',
            stationId: 'fot',
            stationCode: 'FOT',
            sequence: 3,
            arrival: api.server.add(const Duration(minutes: 30)),
          ),
        ],
      );
      final train = PassengerLiveTrain(
        journeyId: 'one',
        serviceDate: journey.serviceDate,
        trainNumber: journey.trainNumber,
        originStopId: 'gph-stop',
        destinationStopId: 'fot-stop',
        scheduledDeparture: journey.stops.first.departure!,
        scheduledArrival: journey.stops.last.arrival!,
        estimatedDeparture: api.server.add(const Duration(minutes: 3)),
        estimatedArrival: api.server.add(const Duration(minutes: 31)),
        freshness: 'CURRENT',
        overdue: false,
        availability: 'RUNNING',
        lastObservedAt: api.server,
        confirmedPlatform: '3',
        confirmedPlatformAt: api.server,
        finalStopId: 'rgm-stop',
      );
      final notice = PassengerNotice(
        journeyId: journey.id,
        serviceDate: journey.serviceDate,
        trainNumber: journey.trainNumber,
        kind: 'ENDS_EARLY',
        originStopId: 'gph-stop',
        destinationStopId: 'fot-stop',
        scheduledDeparture: journey.stops.first.departure!,
        finalStopId: 'rgm-stop',
        confirmedAt: api.server,
      );
      final cancellation = PassengerNotice(
        journeyId: 'cancelled',
        serviceDate: journey.serviceDate,
        trainNumber: 'SIM-1002',
        kind: 'CANCELLED',
        originStopId: 'gph-stop',
        destinationStopId: 'fot-stop',
        scheduledDeparture: journey.stops.first.departure!,
        confirmedAt: api.server,
      );
      final c =
          TrainController(
              api: api,
              cache: MemoryTrainCache(),
              clock: () => api.server,
            )
            ..stations = api.stationList
            ..originId = 'gph'
            ..destinationId = 'fot'
            ..liveTrains = [train]
            ..notices = [notice, cancellation]
            ..saved = PublicTrainCache(
              stations: api.stationList,
              journeys: [journey],
              notices: [notice],
            );
      await tester.pumpWidget(
        MaterialApp(
          theme: srTheme(SrProfile.passenger, Brightness.light),
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          home: Scaffold(body: TrainsPage(controller: c)),
        ),
      );
      expect(find.textContaining('Staff-confirmed change'), findsWidgets);
      expect(
        find.textContaining('SIM-1003 ends early at Ragama'),
        findsOneWidget,
      );
      expect(find.textContaining('SIM-1002'), findsOneWidget);
      await tester.tap(find.byKey(const Key('train-one')));
      await tester.pumpAndSettle();
      expect(find.textContaining('Not served'), findsOneWidget);
      expect(find.byIcon(Icons.train), findsOneWidget);
      c.dispose();
    },
  );

  testWidgets('empty results and partial timetable failure are explained', (
    tester,
  ) async {
    final api = FakeTrainSource();
    final c = TrainController(api: api, cache: MemoryTrainCache())
      ..stations = api.stationList
      ..originId = 'gph'
      ..destinationId = 'fot'
      ..partialError = 'timetable';
    await tester.pumpWidget(
      MaterialApp(
        theme: srTheme(SrProfile.passenger, Brightness.light),
        supportedLocales: AppLocalizations.supportedLocales,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        home: Scaffold(body: TrainsPage(controller: c)),
      ),
    );
    expect(find.textContaining('could not refresh'), findsOneWidget);
    expect(find.textContaining('No trains are scheduled'), findsOneWidget);
    c.dispose();
  });

  test('a cold offline start keeps public tabs usable and states there is no timetable', () async {
    final api = FakeTrainSource()..failLive = true;
    final c = TrainController(api: api, cache: MemoryTrainCache());
    await c.initialize();
    expect(c.offline, isTrue);
    expect(c.stations, isNotEmpty);
    expect(c.hasSavedTimetable, isFalse);
    expect(c.liveTrains, isEmpty);
    c.dispose();
  });

  test('refresh crosses Colombo midnight and reconnect restores current running data', () async {
    final api = FakeTrainSource()..server = DateTime.utc(2026, 10, 18, 18, 29);
    final c =
        TrainController(
            api: api,
            cache: MemoryTrainCache(),
            clock: () => api.server,
          )
          ..originId = 'gph'
          ..destinationId = 'fot';
    final first = c.refresh();
    await Future<void>.delayed(Duration.zero);
    api.respond(0, 'gph', 'one');
    await first;
    expect(api.requestedDates.first, DateTime.utc(2026, 10, 18));

    api.server = api.server.add(const Duration(minutes: 2));
    final nextDay = c.refresh();
    await Future<void>.delayed(Duration.zero);
    api.respond(1, 'gph', 'one');
    await nextDay;
    expect(api.requestedDates, contains(DateTime.utc(2026, 10, 19)));

    api.failLive = true;
    await c.refresh();
    expect(c.offline, isTrue);
    api.failLive = false;
    final recovered = c.refresh();
    await Future<void>.delayed(Duration.zero);
    api.respond(2, 'gph', 'one');
    await recovered;
    expect(c.offline, isFalse);
    expect(c.freshnessFor(c.liveTrains.single), 'CURRENT');
    c.dispose();
  });

  test(
    'an offline reload lists saved schedules only and keeps estimates out',
    () async {
      final api = FakeTrainSource()..failLive = true;
      final schedule = PassengerJourney(
        id: 'saved-journey',
        serviceDate: DateTime.utc(2026, 10, 18),
        trainNumber: 'SIM-1003',
        sourceLabel: 'Fictional demo timetable',
        stops: [
          PassengerStop(
            id: 'gph-stop',
            stationId: 'gph',
            stationCode: 'GPH',
            sequence: 1,
            departure: DateTime.utc(2026, 10, 18, 5, 20),
          ),
          PassengerStop(
            id: 'fot-stop',
            stationId: 'fot',
            stationCode: 'FOT',
            sequence: 2,
            arrival: DateTime.utc(2026, 10, 18, 6, 15),
          ),
        ],
      );
      final cache = MemoryTrainCache(
        PublicTrainCache(
          stations: api.stationList,
          journeys: [schedule],
          timetableDownloadedAt: api.server,
        ),
      );
      final c = TrainController(
        api: api,
        cache: cache,
        clock: () => api.server,
      );
      await c.initialize();
      expect(c.offline, isTrue);
      expect(c.liveTrains, isEmpty);
      final choice = c.savedChoices().single;
      expect(choice.journey.id, schedule.id);
      expect(choice.origin.departure, schedule.stops.first.departure);
      expect(choice.destination.arrival, schedule.stops.last.arrival);
      c.dispose();
    },
  );

  test(
    'timetable and last confirmed notices retain separate download timestamps',
    () async {
      SharedPreferences.setMockInitialValues({});
      final cache = SharedPreferencesTrainCache();
      final timetableAt = DateTime.utc(2026, 10, 18, 4);
      final noticeAt = DateTime.utc(2026, 10, 18, 4, 5);
      final journey = PassengerJourney(
        id: 'saved',
        serviceDate: DateTime.utc(2026, 10, 18),
        trainNumber: 'SIM-1003',
        sourceLabel: 'Fictional demo timetable',
        stops: [
          PassengerStop(
            id: 'gph-stop',
            stationId: 'gph',
            stationCode: 'GPH',
            sequence: 1,
            departure: DateTime.utc(2026, 10, 18, 5),
          ),
          PassengerStop(
            id: 'fot-stop',
            stationId: 'fot',
            stationCode: 'FOT',
            sequence: 2,
            arrival: DateTime.utc(2026, 10, 18, 6),
          ),
        ],
      );
      final notice = PassengerNotice(
        journeyId: 'saved',
        serviceDate: journey.serviceDate,
        trainNumber: journey.trainNumber,
        kind: 'CANCELLED',
        originStopId: 'gph-stop',
        destinationStopId: 'fot-stop',
        scheduledDeparture: journey.stops.first.departure!,
        confirmedAt: noticeAt,
      );
      await cache.saveStations(FakeTrainSource().stationList, timetableAt);
      await cache.saveTimetable([journey], timetableAt);
      await cache.saveNotices([notice], noticeAt, 'gph', 'fot');
      final restored = await cache.read();
      expect(restored.stations, hasLength(3));
      expect(restored.journeys.single.sourceLabel, 'Fictional demo timetable');
      expect(restored.timetableDownloadedAt, timetableAt);
      expect(restored.notices.single.confirmedAt, noticeAt);
      expect(restored.noticesDownloadedAt, noticeAt);
    },
  );
}
