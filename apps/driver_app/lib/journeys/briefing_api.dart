import 'dart:convert';

import 'package:http/http.dart' as http;

import '../auth/auth_service.dart';
import '../platform_client.dart';

class DriverJourney {
  const DriverJourney({
    required this.id,
    required this.serviceDate,
    required this.trainNumber,
    required this.label,
    required this.origin,
    required this.departure,
    required this.destination,
    required this.arrival,
    required this.availability,
  });

  factory DriverJourney.fromJson(Map<String, dynamic> json) => DriverJourney(
    id: json['journeyId'] as String,
    serviceDate: DateTime.parse(json['serviceDate'] as String),
    trainNumber: json['trainNumber'] as String,
    label: json['timetableLabel'] as String,
    origin: json['originName'] as String,
    departure: _date(json['scheduledDeparture']),
    destination: json['destinationName'] as String,
    arrival: _date(json['scheduledArrival']),
    availability: json['availability'] as String,
  );

  /// The same keys the server sends, so a saved copy reads back through [DriverJourney.fromJson].
  Map<String, dynamic> toJson() => {
    'journeyId': id,
    'serviceDate':
        '${serviceDate.year.toString().padLeft(4, '0')}-${serviceDate.month.toString().padLeft(2, '0')}-${serviceDate.day.toString().padLeft(2, '0')}',
    'trainNumber': trainNumber,
    'timetableLabel': label,
    'originName': origin,
    'scheduledDeparture': departure?.toUtc().toIso8601String(),
    'destinationName': destination,
    'scheduledArrival': arrival?.toUtc().toIso8601String(),
    'availability': availability,
  };

  final String id;
  final DateTime serviceDate;
  final String trainNumber;
  final String label;
  final String origin;
  final DateTime? departure;
  final String destination;
  final DateTime? arrival;
  final String availability;
}

class BriefingStop {
  const BriefingStop({
    required this.id,
    required this.station,
    required this.sequence,
    required this.served,
    required this.scheduledArrival,
    required this.scheduledDeparture,
    required this.estimatedArrival,
    required this.estimatedDeparture,
    required this.plannedPlatform,
    required this.confirmedPlatform,
    required this.confirmedPlatformAt,
  });

  factory BriefingStop.fromJson(Map<String, dynamic> json) => BriefingStop(
    id: json['stopId'] as String,
    station: json['stationName'] as String,
    sequence: json['sequence'] as int,
    served: json['served'] as bool,
    scheduledArrival: _date(json['scheduledArrival']),
    scheduledDeparture: _date(json['scheduledDeparture']),
    estimatedArrival: _date(json['estimatedArrival']),
    estimatedDeparture: _date(json['estimatedDeparture']),
    plannedPlatform: json['plannedPlatform'] as String?,
    confirmedPlatform: json['confirmedPlatform'] as String?,
    confirmedPlatformAt: _date(json['confirmedPlatformAt']),
  );

  final String id;
  final String station;
  final int sequence;
  final bool served;
  final DateTime? scheduledArrival;
  final DateTime? scheduledDeparture;
  final DateTime? estimatedArrival;
  final DateTime? estimatedDeparture;
  final String? plannedPlatform;
  final String? confirmedPlatform;
  final DateTime? confirmedPlatformAt;
}

class BriefingChange {
  const BriefingChange({
    required this.kind,
    required this.confirmedAt,
    required this.confirmedBy,
    required this.delayMinutes,
    required this.newPlatform,
    required this.finalStopId,
  });

  factory BriefingChange.fromJson(Map<String, dynamic> json) => BriefingChange(
    kind: json['kind'] as String,
    confirmedAt: DateTime.parse(json['confirmedAt'] as String),
    confirmedBy: json['confirmedByName'] as String?,
    delayMinutes: json['delayMinutes'] as int?,
    newPlatform: json['newPlatform'] as String?,
    finalStopId: json['finalStopId'] as String?,
  );

  final String kind;
  final DateTime confirmedAt;
  final String? confirmedBy;
  final int? delayMinutes;
  final String? newPlatform;
  final String? finalStopId;
}

class DriverBriefing {
  const DriverBriefing({
    required this.id,
    required this.trainNumber,
    required this.serviceDate,
    required this.timetableLabel,
    required this.runningLabel,
    required this.ageingAfterSeconds,
    required this.unknownAfterSeconds,
    required this.observedAt,
    required this.availability,
    required this.finalStopId,
    required this.stops,
    required this.changes,
  });

  factory DriverBriefing.fromJson(Map<String, dynamic> json) {
    final thresholds = json['thresholds'] as Map<String, dynamic>;
    final observation = json['lastObservation'] as Map<String, dynamic>?;
    return DriverBriefing(
      id: json['journeyId'] as String,
      trainNumber: json['trainNumber'] as String,
      serviceDate: DateTime.parse(json['serviceDate'] as String),
      timetableLabel:
          (json['timetable'] as Map<String, dynamic>)['label'] as String,
      runningLabel: json['runningLabel'] as String,
      ageingAfterSeconds: thresholds['ageingAfterSeconds'] as int,
      unknownAfterSeconds: thresholds['unknownAfterSeconds'] as int,
      observedAt: _date(observation?['observedAt']),
      availability: json['availability'] as String,
      finalStopId: json['finalStopId'] as String?,
      stops: [
        for (final stop in json['stops'] as List)
          BriefingStop.fromJson(stop as Map<String, dynamic>),
      ],
      changes: [
        for (final change in json['changes'] as List)
          BriefingChange.fromJson(change as Map<String, dynamic>),
      ],
    );
  }

  final String id;
  final String trainNumber;
  final DateTime serviceDate;
  final String timetableLabel;
  final String runningLabel;
  final int ageingAfterSeconds;
  final int unknownAfterSeconds;
  final DateTime? observedAt;
  final String availability;
  final String? finalStopId;
  final List<BriefingStop> stops;
  final List<BriefingChange> changes;
}

DateTime? _date(Object? value) =>
    value == null ? null : DateTime.parse(value as String);

sealed class DriverRead<T> {
  const DriverRead();
}

class DriverReadOk<T> extends DriverRead<T> {
  const DriverReadOk(this.value, this.etag, this.serverTime);
  final T value;
  final String? etag;
  final DateTime? serverTime;
}

class DriverReadNotModified<T> extends DriverRead<T> {
  const DriverReadNotModified(this.serverTime);
  final DateTime? serverTime;
}

class DriverReadFailure<T> extends DriverRead<T> {
  const DriverReadFailure(this.statusCode, this.message);
  final int? statusCode;
  final String message;
}

/// Thin client for the protected driver reads. It accepts a token provider so tokens are refreshed
/// for every request and never stored in this feature layer.
class DriverBriefingApi {
  DriverBriefingApi(this._baseUrl, this._auth, {http.Client? client})
    : _client = client ?? platformHttpClient();

  final String _baseUrl;
  final AuthService _auth;
  final http.Client _client;

  Future<String?> ownerId() => _auth.ownerId();

  Future<DriverRead<List<DriverJourney>>> journeys(
    DateTime date, {
    String? etag,
  }) async {
    final day =
        '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';
    return _read<List<DriverJourney>>(
      Uri.parse('$_baseUrl/api/driver/journeys?serviceDate=$day'),
      etag: etag,
      parse: (json) => [
        for (final row in json['journeys'] as List)
          DriverJourney.fromJson(row as Map<String, dynamic>),
      ],
    );
  }

  Future<DriverRead<DriverBriefing>> briefing(
    String journeyId, {
    String? etag,
  }) => _read<DriverBriefing>(
    Uri.parse('$_baseUrl/api/driver/journeys/$journeyId/briefing'),
    etag: etag,
    parse: DriverBriefing.fromJson,
  );

  Future<DriverRead<T>> _read<T>(
    Uri uri, {
    required T Function(Map<String, dynamic>) parse,
    String? etag,
  }) async {
    try {
      final token = await _auth.accessToken();
      final headers = <String, String>{'Authorization': 'Bearer $token'};
      if (etag != null) headers['If-None-Match'] = etag;
      final response = await _client.get(uri, headers: headers);
      final serverTime = DateTime.tryParse(
        response.headers['x-smartrail-server-time'] ?? '',
      );
      if (response.statusCode == 304) {
        return DriverReadNotModified<T>(serverTime);
      }
      if (response.statusCode != 200) {
        return DriverReadFailure<T>(
          response.statusCode,
          _problem(response.body, response.statusCode),
        );
      }
      return DriverReadOk<T>(
        parse(jsonDecode(response.body) as Map<String, dynamic>),
        response.headers['etag'],
        serverTime,
      );
    } on AuthSessionRejected {
      return DriverReadFailure<T>(401, 'Your sign-in has expired.');
    } on Exception catch (error) {
      return DriverReadFailure<T>(null, error.toString());
    }
  }

  String _problem(String body, int status) {
    try {
      final json = jsonDecode(body) as Map<String, dynamic>;
      return (json['detail'] ?? json['title'] ?? 'Server answered $status.')
          .toString();
    } catch (_) {
      return 'Server answered $status.';
    }
  }
}
