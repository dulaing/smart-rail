import 'dart:convert';

import 'package:http/http.dart' as http;

import '../platform_client.dart';
import 'train_models.dart';

class ApiRead<T> {
  const ApiRead(this.value, this.etag, this.serverTime);
  final T? value;
  final String? etag;
  final DateTime? serverTime;
}

abstract interface class PassengerTrainSource {
  Future<List<PassengerStation>> stations();
  Future<ApiRead<RouteSnapshot>> live(
    String origin,
    String destination,
    String? etag,
  );
  Future<String?> lastKnownStation(String journeyId);
  Future<List<PassengerJourney>> timetable(DateTime serviceDate);
}

class PassengerTrainApi implements PassengerTrainSource {
  PassengerTrainApi(String baseUrl, {http.Client? client})
    : _base = baseUrl,
      _client = client ?? platformHttpClient();
  final String _base;
  final http.Client _client;
  Uri _uri(String path, [Map<String, String>? query]) =>
      Uri.parse('$_base$path').replace(queryParameters: query);
  @override
  Future<List<PassengerStation>> stations() async {
    final r = await _client
        .get(_uri('/api/stations'))
        .timeout(const Duration(seconds: 12));
    if (r.statusCode != 200)
      throw StateError('Stations unavailable (${r.statusCode}).');
    return (jsonDecode(r.body) as List)
        .map((j) => PassengerStation.fromJson(j as Map<String, dynamic>))
        .toList();
  }

  @override
  Future<ApiRead<RouteSnapshot>> live(
    String origin,
    String destination,
    String? etag,
  ) async {
    final headers = <String, String>{if (etag != null) 'If-None-Match': etag};
    final r = await _client
        .get(
          _uri('/api/live-next-trains', {
            'originId': origin,
            'destinationId': destination,
          }),
          headers: headers,
        )
        .timeout(const Duration(seconds: 12));
    final server = r.headers['x-smartrail-server-time'];
    final serverTime = server == null ? null : DateTime.tryParse(server);
    if (r.statusCode == 304)
      return ApiRead(null, r.headers['etag'] ?? etag, serverTime);
    if (r.statusCode != 200)
      throw StateError('Train times unavailable (${r.statusCode}).');
    final json = jsonDecode(r.body) as Map<String, dynamic>;
    final snapshot = RouteSnapshot.fromJson({
      ...json,
      'serverTime':
          serverTime?.toIso8601String() ??
          DateTime.now().toUtc().toIso8601String(),
    }, DateTime.now().toUtc());
    return ApiRead(snapshot, r.headers['etag'], serverTime);
  }

  @override
  Future<List<PassengerJourney>> timetable(DateTime serviceDate) async {
    final day =
        '${serviceDate.year.toString().padLeft(4, '0')}-${serviceDate.month.toString().padLeft(2, '0')}-${serviceDate.day.toString().padLeft(2, '0')}';
    final r = await _client
        .get(_uri('/api/journeys', {'serviceDate': day}))
        .timeout(const Duration(seconds: 12));
    if (r.statusCode != 200)
      throw StateError('Saved timetable could not refresh (${r.statusCode}).');
    final json = jsonDecode(r.body) as Map<String, dynamic>;
    return (json['journeys'] as List)
        .map((j) => PassengerJourney.fromJson(j as Map<String, dynamic>))
        .toList();
  }

  @override
  Future<String?> lastKnownStation(String journeyId) async {
    final r = await _client
        .get(_uri('/api/journeys/$journeyId/running'))
        .timeout(const Duration(seconds: 12));
    if (r.statusCode != 200) {
      throw StateError('Train position unavailable (${r.statusCode}).');
    }
    final json = jsonDecode(r.body) as Map<String, dynamic>;
    final observation = json['lastObservation'] as Map<String, dynamic>?;
    return observation?['lastKnownStationId'] as String?;
  }

  void close() => _client.close();
}
