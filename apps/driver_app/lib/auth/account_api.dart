import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:http/http.dart' as http;

import '../platform_client.dart';

/// The signed-in person's SmartRail account, from `GET /api/me`.
class Account {
  const Account({
    required this.displayName,
    required this.roles,
    required this.permissions,
  });

  factory Account.fromJson(Map<String, dynamic> json) => Account(
    displayName: json['displayName'] as String,
    roles: [
      for (final g in json['grants'] as List) (g as Map)['role'] as String,
    ],
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

/// A previously signed-in identity whose session could not be refreshed because the network is
/// unavailable. It grants access only to owner-bound saved briefing files.
class AccountOffline extends AccountResult {
  const AccountOffline(this.ownerId);
  final String ownerId;
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
  const AccountFailed(this.message, {this.statusCode});
  final String message;
  final int? statusCode;
}

abstract class AccountApi {
  Future<AccountResult> fetchMe(String accessToken);
}

class HttpAccountApi implements AccountApi {
  HttpAccountApi(this._baseUrl, {http.Client? client})
    : _client = client ?? platformHttpClient();

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
      return AccountFailed(
        'The server answered ${response.statusCode}.',
        statusCode: response.statusCode,
      );
    } catch (error) {
      debugPrint('GET /api/me failed: ${error.runtimeType}: $error');
      return const AccountFailed('Could not reach SmartRail.');
    }
  }
}
