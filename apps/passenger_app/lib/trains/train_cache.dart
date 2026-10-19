import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import 'train_models.dart';

class PublicTrainCache {
  const PublicTrainCache({
    this.stations = const [],
    this.journeys = const [],
    this.notices = const [],
    this.timetableDownloadedAt,
    this.stationsDownloadedAt,
    this.noticesDownloadedAt,
  });
  final List<PassengerStation> stations;
  final List<PassengerJourney> journeys;
  final List<PassengerNotice> notices;
  final DateTime? timetableDownloadedAt,
      stationsDownloadedAt,
      noticesDownloadedAt;
}

abstract interface class PassengerTrainCache {
  Future<PublicTrainCache> read();
  Future<void> saveStations(List<PassengerStation> stations, DateTime at);
  Future<void> saveTimetable(List<PassengerJourney> journeys, DateTime at);
  Future<void> saveNotices(
    List<PassengerNotice> notices,
    DateTime at,
    String originId,
    String destinationId,
  );
}

class SharedPreferencesTrainCache implements PassengerTrainCache {
  static const _key = 'public.train-data.v1';
  @override
  Future<PublicTrainCache> read() async {
    final raw = (await SharedPreferences.getInstance()).getString(_key);
    if (raw == null) return const PublicTrainCache();
    try {
      final j = jsonDecode(raw) as Map<String, dynamic>;
      return PublicTrainCache(
        stations: ((j['stations'] ?? []) as List)
            .map((v) => PassengerStation.fromJson(v))
            .toList(),
        journeys: ((j['journeys'] ?? []) as List)
            .map((v) => PassengerJourney.fromJson(v))
            .toList(),
        notices: ((j['notices'] ?? []) as List)
            .map((v) => PassengerNotice.fromJson(v))
            .toList(),
        timetableDownloadedAt: _readTime(
          j['timetableDownloadedAt'] ?? j['downloadedAt'],
        ),
        stationsDownloadedAt: _readTime(
          j['stationsDownloadedAt'] ?? j['downloadedAt'],
        ),
        noticesDownloadedAt: _readTime(
          j['noticesDownloadedAt'] ?? j['downloadedAt'],
        ),
      );
    } catch (_) {
      return const PublicTrainCache();
    }
  }

  @override
  Future<void> saveStations(
    List<PassengerStation> stations,
    DateTime at,
  ) async {
    final c = await read();
    await _write(
      stations: stations,
      journeys: c.journeys,
      notices: c.notices,
      timetableAt: c.timetableDownloadedAt,
      stationsAt: at,
      noticesAt: c.noticesDownloadedAt,
    );
  }

  @override
  Future<void> saveTimetable(
    List<PassengerJourney> journeys,
    DateTime at,
  ) async {
    final c = await read();
    final byId = {
      for (final j in c.journeys) j.id: j,
      for (final j in journeys) j.id: j,
    };
    final sorted = byId.values.toList()
      ..sort((a, b) => a.serviceDate.compareTo(b.serviceDate));
    // Bound local public data: latest four dates and at most 500 journeys.
    final dates = sorted.map((j) => _dateKey(j.serviceDate)).toSet().toList()
      ..sort();
    final keepDates = dates
        .skip(dates.length > 4 ? dates.length - 4 : 0)
        .toSet();
    final bounded = sorted
        .where((j) => keepDates.contains(_dateKey(j.serviceDate)))
        .take(500)
        .toList();
    await _write(
      stations: c.stations,
      journeys: bounded,
      notices: c.notices,
      timetableAt: at,
      stationsAt: c.stationsDownloadedAt,
      noticesAt: c.noticesDownloadedAt,
    );
  }

  @override
  Future<void> saveNotices(
    List<PassengerNotice> notices,
    DateTime at,
    String originId,
    String destinationId,
  ) async {
    final c = await read();
    final unrelated = c.notices.where((old) {
      final journey = c.journeys
          .where((j) => j.id == old.journeyId)
          .firstOrNull;
      if (journey == null) return false;
      final from = journey.stops
          .where((s) => s.id == old.originStopId)
          .firstOrNull;
      final to = journey.stops
          .where((s) => s.id == old.destinationStopId)
          .firstOrNull;
      return from == null ||
          to == null ||
          from.stationId != originId ||
          to.stationId != destinationId;
    });
    final byRouteJourney = {
      for (final n in unrelated) _noticeKey(n): n,
      for (final n in notices) _noticeKey(n): n,
    };
    final ordered = byRouteJourney.values.toList()
      ..sort((a, b) => b.scheduledDeparture.compareTo(a.scheduledDeparture));
    await _write(
      stations: c.stations,
      journeys: c.journeys,
      notices: ordered.take(100).toList(),
      timetableAt: c.timetableDownloadedAt,
      stationsAt: c.stationsDownloadedAt,
      noticesAt: at,
    );
  }

  Future<void> _write({
    required List<PassengerStation> stations,
    required List<PassengerJourney> journeys,
    required List<PassengerNotice> notices,
    required DateTime? timetableAt,
    required DateTime? stationsAt,
    required DateTime? noticesAt,
  }) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(
      _key,
      jsonEncode({
        'stations': stations.map((s) => s.toJson()).toList(),
        'journeys': journeys.map((j) => j.toJson()).toList(),
        'notices': notices.map((n) => n.toJson()).toList(),
        'timetableDownloadedAt': timetableAt?.toUtc().toIso8601String(),
        'stationsDownloadedAt': stationsAt?.toUtc().toIso8601String(),
        'noticesDownloadedAt': noticesAt?.toUtc().toIso8601String(),
      }),
    );
  }
}

String _dateKey(DateTime d) => '${d.year}-${d.month}-${d.day}';
DateTime? _readTime(dynamic value) =>
    value == null ? null : DateTime.parse(value as String);
String _noticeKey(PassengerNotice n) =>
    '${n.journeyId}|${n.originStopId}|${n.destinationStopId}';
