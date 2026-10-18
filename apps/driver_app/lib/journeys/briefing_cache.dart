import 'dart:convert';
import 'dart:io';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:path_provider/path_provider.dart';

import 'briefing_api.dart';

/// Stores the driver's downloaded briefing in app-private storage and keeps its owner marker in
/// secure storage. This cache is for read-only recovery; it never contains report drafts.
class BriefingCache {
  BriefingCache({FlutterSecureStorage? secureStorage})
    : _secure = secureStorage ?? const FlutterSecureStorage();

  static const _ownerKey = 'driver_briefing_cache_owner';
  final FlutterSecureStorage _secure;

  Future<File> _file() async {
    final directory = await getApplicationDocumentsDirectory();
    return File(
      '${directory.path}${Platform.pathSeparator}driver-briefings.json',
    );
  }

  Future<void> save(
    String ownerId,
    DriverJourney journey,
    DriverBriefing briefing,
  ) async {
    final existingOwner = await _secure.read(key: _ownerKey);
    if (existingOwner != null && existingOwner != ownerId) await clear();
    final rows = await readAll(ownerId);
    rows.removeWhere((row) => row['journeyId'] == journey.id);
    rows.add({
      'journeyId': journey.id,
      'serviceDate': _day(journey.serviceDate),
      'trainNumber': journey.trainNumber,
      'timetableLabel': journey.label,
      'originName': journey.origin,
      'scheduledDeparture': journey.departure?.toUtc().toIso8601String(),
      'destinationName': journey.destination,
      'scheduledArrival': journey.arrival?.toUtc().toIso8601String(),
      'availability': journey.availability,
      'briefing': briefing.toJson(),
      'downloadedAt': DateTime.now().toUtc().toIso8601String(),
      'checkedAt': DateTime.now().toUtc().toIso8601String(),
    });
    await _secure.write(key: _ownerKey, value: ownerId);
    await (await _file()).writeAsString(
      jsonEncode({'schemaVersion': 1, 'ownerId': ownerId, 'journeys': rows}),
    );
  }

  Future<void> touch(String ownerId, String journeyId) async {
    final rows = await readAll(ownerId);
    final row = rows.where((item) => item['journeyId'] == journeyId);
    if (row.isEmpty) return;
    row.first['checkedAt'] = DateTime.now().toUtc().toIso8601String();
    await (await _file()).writeAsString(
      jsonEncode({'schemaVersion': 1, 'ownerId': ownerId, 'journeys': rows}),
    );
  }

  Future<List<Map<String, dynamic>>> readAll(String ownerId) async {
    if (await _secure.read(key: _ownerKey) != ownerId) return [];
    try {
      final file = await _file();
      if (!await file.exists()) return [];
      final data =
          jsonDecode(await file.readAsString()) as Map<String, dynamic>;
      if (data['schemaVersion'] != 1 || data['ownerId'] != ownerId) return [];
      return [
        for (final row in data['journeys'] as List)
          Map<String, dynamic>.from(row as Map),
      ];
    } catch (_) {
      return [];
    }
  }

  /// The saved rows that remain after a successful online list for [day] left some of that day's
  /// saved journeys out. An assignment removed on the server must not leave readable content behind.
  static List<Map<String, dynamic>> withoutUnlisted(
    List<Map<String, dynamic>> rows,
    String day,
    Set<String> listedIds,
  ) => [
    for (final row in rows)
      if (row['serviceDate'] != day || listedIds.contains(row['journeyId']))
        row,
  ];

  /// Drops saved briefings for [serviceDate] that the server no longer lists for this driver.
  /// Only call it with a successful online list, never after a connection failure.
  Future<void> removeUnlisted(
    String ownerId,
    DateTime serviceDate,
    Set<String> listedIds,
  ) async {
    final rows = await readAll(ownerId);
    final kept = withoutUnlisted(rows, _day(serviceDate), listedIds);
    if (kept.length == rows.length) return;
    if (kept.isEmpty) return clear();
    await (await _file()).writeAsString(
      jsonEncode({'schemaVersion': 1, 'ownerId': ownerId, 'journeys': kept}),
    );
  }

  Future<bool> hasSaved(String ownerId) async =>
      (await readAll(ownerId)).isNotEmpty;

  Future<void> clear() async {
    await _secure.delete(key: _ownerKey);
    final file = await _file();
    if (await file.exists()) await file.delete();
  }
}

String _day(DateTime date) =>
    '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';

// Model serialization stays local to this small, versioned cache format.
extension BriefingCacheJson on DriverBriefing {
  Map<String, dynamic> toJson() => {
    'journeyId': id,
    'serviceDate': _day(serviceDate),
    'trainNumber': trainNumber,
    'timetable': {'label': timetableLabel},
    'runningLabel': runningLabel,
    'thresholds': {
      'ageingAfterSeconds': ageingAfterSeconds,
      'unknownAfterSeconds': unknownAfterSeconds,
    },
    'lastObservation': {'observedAt': observedAt?.toUtc().toIso8601String()},
    'availability': availability,
    'finalStopId': finalStopId,
    'stops': [
      for (final stop in stops)
        {
          'stopId': stop.id,
          'stationName': stop.station,
          'sequence': stop.sequence,
          'served': stop.served,
          'scheduledArrival': stop.scheduledArrival?.toUtc().toIso8601String(),
          'scheduledDeparture': stop.scheduledDeparture
              ?.toUtc()
              .toIso8601String(),
          'estimatedArrival': stop.estimatedArrival?.toUtc().toIso8601String(),
          'estimatedDeparture': stop.estimatedDeparture
              ?.toUtc()
              .toIso8601String(),
          'plannedPlatform': stop.plannedPlatform,
          'confirmedPlatform': stop.confirmedPlatform,
          'confirmedPlatformAt': stop.confirmedPlatformAt
              ?.toUtc()
              .toIso8601String(),
        },
    ],
    'changes': [
      for (final change in changes)
        {
          'kind': change.kind,
          'confirmedAt': change.confirmedAt.toUtc().toIso8601String(),
          'confirmedByName': change.confirmedBy,
          'delayMinutes': change.delayMinutes,
          'newPlatform': change.newPlatform,
          'finalStopId': change.finalStopId,
        },
    ],
  };
}
