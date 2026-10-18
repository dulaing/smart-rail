import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;
import 'package:cronet_http/cronet_http.dart';

/// The signed-in person's SmartRail account, from `GET /api/me`.
class Account {
  const Account({
    required this.displayName,
    required this.roles,
    required this.permissions,
  });

  factory Account.fromJson(Map<String, dynamic> json) => Account(
    displayName: json['displayName'] as String,
    roles: [for (final g in json['grants'] as List) (g as Map)['role'] as String],
    permissions: [
      for (final p in json['permissions'] as List) (p as Map)['code'] as String,
    ],
  );

  final String displayName;
  final List<String> roles;
  final List<String> permissions;
}

sealed class AccountResult {
  const AccountResult();
}

class AccountFound extends AccountResult {
  const AccountFound(this.account);
  final Account account;
}

/// Signed in with Keycloak, but no enabled SmartRail account exists for this identity.
class AccountMissing extends AccountResult {
  const AccountMissing();
}

/// The API rejected the access token (401): the session ended and the person must sign in again.
class AccountExpired extends AccountResult {
  const AccountExpired();
}

class AccountFailed extends AccountResult {
  const AccountFailed(this.message);
  final String message;
}

abstract class AccountApi {
  Future<AccountResult> fetchMe(String accessToken);
}

class HttpAccountApi implements AccountApi {
  HttpAccountApi(this._baseUrl, {http.Client? client})
    : _client = client ?? _platformClient();

  // On Android, Dart's own HTTP client trusts only the built-in system CAs. Cronet checks
  // certificates through Android, so it follows network_security_config.xml like AppAuth does.
  static http.Client _platformClient() =>
      Platform.isAndroid ? CronetClient.defaultCronetEngine() : http.Client();

  final String _baseUrl;
  final http.Client _client;

  @override
  Future<AccountResult> fetchMe(String accessToken) async {
    try {
      final response = await _client.get(
        Uri.parse('$_baseUrl/api/me'),
        headers: {'Authorization': 'Bearer $accessToken'},
      );
      if (response.statusCode == 200) {
        return AccountFound(
          Account.fromJson(jsonDecode(response.body) as Map<String, dynamic>),
        );
      }
      if (response.statusCode == 401) {
        return const AccountExpired();
      }
      if (response.statusCode == 403) {
        return const AccountMissing();
      }
      return AccountFailed('The server answered ${response.statusCode}.');
    } catch (error) {
      debugPrint('GET /api/me failed: ${error.runtimeType}: $error');
      return const AccountFailed('Could not reach SmartRail.');
    }
  }
}
