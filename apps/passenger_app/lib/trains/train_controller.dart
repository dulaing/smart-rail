import 'dart:async';

import 'package:flutter/foundation.dart';

import 'train_api.dart';
import 'train_cache.dart';
import 'train_models.dart';

class TrainController extends ChangeNotifier {
  TrainController({
    required PassengerTrainSource api,
    required PassengerTrainCache cache,
    DateTime Function()? clock,
  }) : _api = api,
       _cache = cache,
       _clock = clock ?? DateTime.now;
  final PassengerTrainSource _api;
  final PassengerTrainCache _cache;
  final DateTime Function() _clock;
  static const _colomboOffset = Duration(hours: 5, minutes: 30);
  PublicTrainCache saved = const PublicTrainCache();
  List<PassengerStation> stations = const [];
  List<PassengerLiveTrain> liveTrains = const [];
  List<PassengerNotice> notices = const [];
  String? originId, destinationId, liveError, partialError, _etag;
  DateTime? observedAt, _serverTime, _receivedAt;
  bool loadingStations = false,
      loadingRoute = false,
      offline = false,
      paused = false;
  int _routeVersion = 0;
  bool _requestActive = false, _disposed = false;
  Timer? _poll, _ageTicker;
  final Map<String, String> _etags = {};
  final Map<String, RouteSnapshot> _routeSnapshots = {};
  String _routeKey([String? origin, String? destination]) =>
      '${origin ?? originId}|${destination ?? destinationId}';
  Duration get evidenceAge => observedAt == null
      ? const Duration(days: 1)
      : now.difference(observedAt!);
  DateTime get now {
    final base = _serverTime;
    final received = _receivedAt;
    return base == null || received == null
        ? _clock().toUtc()
        : base.add(_clock().toUtc().difference(received));
  }

  String get freshness {
    if (observedAt == null || evidenceAge >= const Duration(seconds: 240))
      return 'UNKNOWN';
    if (evidenceAge >= const Duration(seconds: 180)) return 'AGEING';
    return 'CURRENT';
  }

  bool get hasSavedTimetable => saved.journeys.isNotEmpty;
  String freshnessFor(PassengerLiveTrain train) {
    if (train.lastObservedAt == null) return 'UNKNOWN';
    final age = now.difference(train.lastObservedAt!);
    if (age >= const Duration(seconds: 240)) return 'UNKNOWN';
    if (age >= const Duration(seconds: 180)) return 'AGEING';
    return 'CURRENT';
  }

  PassengerJourney? cachedJourney(String id) {
    for (final j in saved.journeys) {
      if (j.id == id) return j;
    }
    return null;
  }

  Future<PassengerJourney?> loadJourney(String id, DateTime serviceDate) async {
    final found = cachedJourney(id);
    if (found != null) return found;
    try {
      final journeys = await _api.timetable(serviceDate);
      await _cache.saveTimetable(journeys, DateTime.now().toUtc());
      saved = await _cache.read();
      _notify();
      return cachedJourney(id);
    } catch (_) {
      return null;
    }
  }

  Future<String?> loadLastKnownStation(String journeyId) async {
    try {
      return await _api.lastKnownStation(journeyId);
    } catch (_) {
      return null;
    }
  }

  Future<void> initialize() async {
    try {
      saved = await _cache.read();
    } catch (_) {
      saved = const PublicTrainCache();
    }
    stations = saved.stations;
    _notify();
    loadingStations = stations.isEmpty;
    try {
      final fresh = await _api.stations();
      stations = fresh;
      saved = PublicTrainCache(
        stations: fresh,
        journeys: saved.journeys,
        notices: saved.notices,
        timetableDownloadedAt: saved.timetableDownloadedAt,
        stationsDownloadedAt: DateTime.now().toUtc(),
        noticesDownloadedAt: saved.noticesDownloadedAt,
      );
      await _cache.saveStations(fresh, DateTime.now().toUtc());
    } catch (_) {
      /* A previously saved station list remains useful offline. */
    }
    if (stations.isNotEmpty) {
      originId ??=
          stations.where((s) => s.code == 'GPH').firstOrNull?.id ??
          stations.first.id;
      destinationId ??=
          stations.where((s) => s.code == 'FOT').firstOrNull?.id ??
          stations.firstWhere((s) => s.id != originId).id;
    }
    loadingStations = false;
    _poll ??= Timer.periodic(const Duration(seconds: 30), (_) {
      if (!paused) unawaited(refresh());
    });
    _ageTicker ??= Timer.periodic(const Duration(seconds: 1), (_) => _notify());
    _notify();
    if (originId != null && destinationId != null) await refresh();
  }

  Future<void> setRoute(String origin, String destination) async {
    if (origin == destination) return;
    originId = origin;
    destinationId = destination;
    _routeVersion++;
    _requestActive = false;
    _etag = _etags[_routeKey(origin, destination)];
    final savedRoute = _routeSnapshots[_routeKey(origin, destination)];
    liveTrains = savedRoute?.trains ?? const [];
    notices = savedRoute?.notices ?? const [];
    observedAt = savedRoute?.observedAt;
    liveError = null;
    partialError = null;
    _notify();
    await refresh();
  }

  Future<void> refresh() async {
    final origin = originId, destination = destinationId;
    if (origin == null ||
        destination == null ||
        paused ||
        _requestActive ||
        _disposed)
      return;
    final version = _routeVersion;
    _requestActive = true;
    loadingRoute = liveTrains.isEmpty && !offline;
    _notify();
    try {
      final read = await _api.live(origin, destination, _etag);
      if (_disposed || version != _routeVersion) return;
      offline = false;
      liveError = null;
      partialError = null;
      if (read.serverTime != null) {
        _serverTime = read.serverTime!.toUtc();
        _receivedAt = _clock().toUtc();
      }
      if (read.etag != null) {
        _etag = read.etag;
        _etags[_routeKey(origin, destination)] = read.etag!;
      }
      final fresh = read.value;
      if (fresh != null) {
        liveTrains = fresh.trains;
        notices = fresh.notices;
        observedAt = fresh.observedAt;
        _routeSnapshots[_routeKey(origin, destination)] = fresh;
        try {
          await _cache.saveNotices(
            fresh.notices,
            fresh.downloadedAt,
            origin,
            destination,
          );
        } catch (_) {
          partialError = 'notices';
        }
      }
    } catch (_) {
      if (_disposed || version != _routeVersion) return;
      offline = true;
      liveError = 'connection';
      liveTrains = const [];
      observedAt = null;
    }
    if (_disposed || version != _routeVersion) return;
    // The scheduled source is a separate public read; a running failure must not prevent it from refreshing.
    try {
      final localToday = now.add(_colomboOffset);
      final day = DateTime.utc(
        localToday.year,
        localToday.month,
        localToday.day,
      );
      final journeys = <PassengerJourney>[];
      for (var offset = 0; offset <= 2; offset++) {
        journeys.addAll(await _api.timetable(day.add(Duration(days: offset))));
      }
      await _cache.saveTimetable(journeys, DateTime.now().toUtc());
      saved = await _cache.read();
      if (offline) notices = _savedNoticesForRoute();
    } catch (_) {
      partialError ??= 'timetable';
      try {
        saved = await _cache.read();
        if (offline) notices = _savedNoticesForRoute();
      } catch (_) {
        /* Keep whichever public snapshot was already loaded. */
      }
    } finally {
      if (version == _routeVersion) {
        loadingRoute = false;
        _requestActive = false;
        _notify();
      }
    }
  }

  List<OfflineTrainChoice> savedChoices() {
    final origin = originId, destination = destinationId;
    if (origin == null || destination == null) return const [];
    final at = now;
    final results = <OfflineTrainChoice>[];
    for (final journey in saved.journeys) {
      final stops = journey.stops;
      OfflineTrainChoice? choice;
      for (var i = 0; i < stops.length; i++) {
        if (stops[i].stationId != origin ||
            stops[i].departure == null ||
            stops[i].departure!.isBefore(at))
          continue;
        for (var j = i + 1; j < stops.length; j++) {
          if (stops[j].stationId == destination && stops[j].arrival != null) {
            choice = OfflineTrainChoice(
              journey: journey,
              origin: stops[i],
              destination: stops[j],
            );
            break;
          }
        }
        if (choice != null) break;
      }
      if (choice != null) results.add(choice);
    }
    results.sort((a, b) => a.origin.departure!.compareTo(b.origin.departure!));
    return results.take(3).toList();
  }

  List<PassengerNotice> _savedNoticesForRoute() {
    final origin = originId, destination = destinationId;
    if (origin == null || destination == null) return const [];
    return saved.notices.where((notice) {
      final journey = cachedJourney(notice.journeyId);
      if (journey == null) return false;
      final from = journey.stops
          .where((s) => s.id == notice.originStopId)
          .firstOrNull;
      final to = journey.stops
          .where((s) => s.id == notice.destinationStopId)
          .firstOrNull;
      if (from == null || to == null) return false;
      return from.stationId == origin &&
          to.stationId == destination &&
          from.sequence < to.sequence;
    }).toList();
  }

  PassengerStation? station(String? id) {
    for (final s in stations) {
      if (s.id == id) return s;
    }
    return null;
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    _poll?.cancel();
    _ageTicker?.cancel();
    super.dispose();
  }
}

class OfflineTrainChoice {
  const OfflineTrainChoice({
    required this.journey,
    required this.origin,
    required this.destination,
  });
  final PassengerJourney journey;
  final PassengerStop origin, destination;
}
