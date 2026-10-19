import 'dart:convert';

import 'package:http/http.dart' as http;

import '../platform_client.dart';
import 'food_models.dart';

enum FoodReadFailure {
  /// 503 PROVIDER_UNAVAILABLE: the food provider is down. Train information is unaffected.
  providerUnavailable,

  /// 404: the journey or collection point is unknown to SmartRail.
  notFound,

  /// Timeout or no connection.
  connection,
  other,
}

class FoodReadException implements Exception {
  const FoodReadException(this.failure, {this.code, this.status});
  final FoodReadFailure failure;
  final String? code;
  final int? status;

  @override
  String toString() => 'FoodReadException($failure, $code, $status)';
}

/// The public food reads. They need no account and never reach RailBites directly.
abstract interface class FoodSource {
  Future<CollectionOptions> collectionOptions(String journeyId);
  Future<FoodMenu> menu(String collectionPointId);
}

class FoodApi implements FoodSource {
  FoodApi(String baseUrl, {http.Client? client}) : _base = baseUrl, _client = client ?? platformHttpClient();

  final String _base;
  final http.Client _client;

  @override
  Future<CollectionOptions> collectionOptions(String journeyId) async => CollectionOptions.fromJson(
    await _get('/api/journeys/${Uri.encodeComponent(journeyId)}/collection-options'),
  );

  @override
  Future<FoodMenu> menu(String collectionPointId) async =>
      FoodMenu.fromJson(await _get('/api/food/collection-points/${Uri.encodeComponent(collectionPointId)}/menu'));

  Future<Map<String, dynamic>> _get(String path) async {
    final http.Response response;
    try {
      response = await _client.get(Uri.parse('$_base$path')).timeout(const Duration(seconds: 12));
    } catch (_) {
      throw const FoodReadException(FoodReadFailure.connection);
    }
    if (response.statusCode == 200) {
      try {
        return jsonDecode(response.body) as Map<String, dynamic>;
      } catch (_) {
        throw FoodReadException(FoodReadFailure.other, status: response.statusCode);
      }
    }
    final code = problemCode(response.body);
    throw FoodReadException(switch (response.statusCode) {
      404 => FoodReadFailure.notFound,
      503 => FoodReadFailure.providerUnavailable,
      _ => FoodReadFailure.other,
    }, code: code, status: response.statusCode);
  }

  void close() => _client.close();
}

/// The stable `code` extension of an RFC 9457 problem body, or null when there is none.
String? problemCode(String body) {
  try {
    final json = jsonDecode(body);
    return json is Map<String, dynamic> ? json['code'] as String? : null;
  } catch (_) {
    return null;
  }
}
