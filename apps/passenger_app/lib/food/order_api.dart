import 'dart:convert';

import 'package:http/http.dart' as http;

import '../platform_client.dart';
import 'food_api.dart';
import 'order_models.dart';

/// What came back from one authenticated order call. [status] is null when no HTTP answer arrived
/// (timeout, connection lost): then the caller cannot know whether the server processed it.
class OrderReply {
  const OrderReply({this.status, this.json, this.code});

  /// A reply that never arrived.
  const OrderReply.lost() : status = null, json = null, code = null;

  final int? status;
  final Map<String, dynamic>? json;

  /// The stable problem `code`, when the body carried one.
  final String? code;

  bool get lost => status == null;

  /// The order in a 200/201/202 body, or null if the body is not an order.
  FoodOrder? get order {
    final body = json;
    if (body == null || body['id'] == null || body['status'] == null) return null;
    try {
      return FoodOrder.fromJson(body);
    } catch (_) {
      return null;
    }
  }

  /// The `orders` of a list body.
  List<FoodOrder>? get orders {
    final list = json?['orders'];
    if (list is! List) return null;
    try {
      return [for (final o in list) FoodOrder.fromJson(o as Map<String, dynamic>)];
    } catch (_) {
      return null;
    }
  }
}

/// The signed-in order calls. Every call takes the bearer token; the account is the token's own.
abstract interface class FoodOrderSource {
  Future<OrderReply> place(String token, CanonicalOrder order, String clientRequestKey);
  Future<OrderReply> read(String token, String orderId);
  Future<OrderReply> list(String token);
  Future<OrderReply> cancel(String token, String orderId);
}

class FoodOrderApi implements FoodOrderSource {
  FoodOrderApi(String baseUrl, {http.Client? client}) : _base = baseUrl, _client = client ?? platformHttpClient();

  final String _base;
  final http.Client _client;

  @override
  Future<OrderReply> place(String token, CanonicalOrder order, String clientRequestKey) => _send(
    'POST',
    '/api/food/orders',
    token,
    body: jsonEncode(order.toRequestJson(clientRequestKey)),
  );

  @override
  Future<OrderReply> read(String token, String orderId) =>
      _send('GET', '/api/food/orders/${Uri.encodeComponent(orderId)}', token);

  @override
  Future<OrderReply> list(String token) => _send('GET', '/api/food/orders?limit=100', token);

  @override
  Future<OrderReply> cancel(String token, String orderId) =>
      _send('POST', '/api/food/orders/${Uri.encodeComponent(orderId)}/cancellation', token);

  Future<OrderReply> _send(String method, String path, String token, {String? body}) async {
    final request = http.Request(method, Uri.parse('$_base$path'))
      ..headers['Authorization'] = 'Bearer $token'
      ..headers['Accept'] = 'application/json';
    if (body != null) {
      request.headers['Content-Type'] = 'application/json';
      request.body = body;
    }
    final http.Response response;
    try {
      response = await http.Response.fromStream(await _client.send(request).timeout(const Duration(seconds: 15)));
    } catch (_) {
      return const OrderReply.lost();
    }
    Map<String, dynamic>? json;
    try {
      final decoded = jsonDecode(response.body);
      if (decoded is Map<String, dynamic>) json = decoded;
    } catch (_) {
      // A non-JSON body (for example a proxy error page) has no usable content.
    }
    return OrderReply(status: response.statusCode, json: json, code: json == null ? null : problemCode(response.body));
  }

  void close() => _client.close();
}
