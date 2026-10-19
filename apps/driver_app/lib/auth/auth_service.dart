import 'dart:convert';

import 'package:flutter_appauth/flutter_appauth.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Where Keycloak and the API are, and how this app is registered as a public client.
///
/// The client has no secret and an exact redirect URI (`manifestPlaceholders` in
/// `android/app/build.gradle.kts` must use the same scheme). Override a value for a device with
/// `--dart-define=ISSUER=...`; the issuer must be the exact URL Keycloak puts in its tokens.
class AuthConfig {
  const AuthConfig({
    this.issuer = const String.fromEnvironment(
      'ISSUER',
      defaultValue: 'https://localhost:8080/realms/smartrail',
    ),
    this.apiBaseUrl = const String.fromEnvironment(
      'API_URL',
      defaultValue: 'https://localhost:7074',
    ),
    this.clientId = 'smartrail-driver-app',
    this.redirectUrl = 'com.smartrail.driver:/oauthredirect',
    this.postLogoutRedirectUrl = 'com.smartrail.driver:/logout',
  });

  final String issuer;
  final String apiBaseUrl;
  final String clientId;
  final String redirectUrl;
  final String postLogoutRedirectUrl;
}

/// Signs in through the system browser and keeps the session fresh.
abstract class AuthService {
  /// Reopens a previous session from secure storage, if there is one.
  Future<bool> restore();

  /// Stable subject from the signed-in ID token, used only to bind private offline data.
  Future<String?> ownerId();

  /// Opens the Keycloak sign-in page. Throws if the person cancels or sign-in fails.
  Future<void> signIn();

  /// An access token for the API, refreshed through Keycloak when it is about to expire.
  Future<String> accessToken();

  /// Ends the Keycloak session and clears every stored token.
  Future<void> signOut();
}

class KeycloakAuthService implements AuthService {
  KeycloakAuthService(
    this._config, {
    FlutterAppAuth? appAuth,
    FlutterSecureStorage? storage,
    DateTime Function()? now,
  }) : _appAuth = appAuth ?? const FlutterAppAuth(),
       _storage = storage ?? const FlutterSecureStorage(),
       _now = now ?? DateTime.now;

  static const _refreshTokenKey = 'refresh_token';
  static const _idTokenKey = 'id_token';
  static const _ownerIdKey = 'offline_owner_id';
  static const _scopes = ['openid', 'profile'];
  static const _refreshMargin = Duration(seconds: 30);

  final AuthConfig _config;
  final FlutterAppAuth _appAuth;
  final FlutterSecureStorage _storage;
  final DateTime Function() _now;

  // The access token lives in memory only; only the refresh and ID tokens are stored.
  String? _accessToken;
  DateTime? _accessTokenExpiry;

  @override
  Future<bool> restore() async {
    if (await _storage.read(key: _refreshTokenKey) == null) {
      return false;
    }
    try {
      await accessToken();
      return true;
    } on AuthSessionRejected {
      await _clear();
      return false;
    } on FlutterAppAuthPlatformException catch (error) {
      if (error.platformErrorDetails.error ==
          FlutterAppAuthOAuthError.invalidGrant) {
        await _clear();
        return false;
      }
      if (error.platformErrorDetails.error == null) {
        throw const AuthRestoreUnavailable();
      }
      // Network and discovery failures do not prove the refresh token was revoked.
      rethrow;
    } catch (_) {
      // Keep the owner and saved briefing during a transport failure.
      rethrow;
    }
  }

  @override
  Future<String?> ownerId() async {
    final saved = await _storage.read(key: _ownerIdKey);
    if (saved != null) return saved;
    final idToken = await _storage.read(key: _idTokenKey);
    if (idToken == null) return null;
    try {
      final claims = jsonDecode(
        utf8.decode(
          base64Url.decode(base64Url.normalize(idToken.split('.')[1])),
        ),
      ) as Map<String, dynamic>;
      final subject = claims['sub'];
      if (subject is! String || subject.isEmpty) return null;
      await _storage.write(key: _ownerIdKey, value: subject);
      return subject;
    } catch (_) {
      return null;
    }
  }

  @override
  Future<void> signIn() async {
    final response = await _appAuth.authorizeAndExchangeCode(
      AuthorizationTokenRequest(
        _config.clientId,
        _config.redirectUrl,
        issuer: _config.issuer,
        scopes: _scopes,
      ),
    );
    await _keep(
      accessToken: response.accessToken,
      expiry: response.accessTokenExpirationDateTime,
      refreshToken: response.refreshToken,
      idToken: response.idToken,
    );
  }

  @override
  Future<String> accessToken() async {
    final token = _accessToken;
    final expiry = _accessTokenExpiry;
    if (token != null &&
        expiry != null &&
        expiry.isAfter(_now().add(_refreshMargin))) {
      return token;
    }

    final refreshToken = await _storage.read(key: _refreshTokenKey);
    if (refreshToken == null) {
      throw StateError('Not signed in.');
    }
    final TokenResponse response;
    try {
      response = await _appAuth.token(
        TokenRequest(
          _config.clientId,
          _config.redirectUrl,
          issuer: _config.issuer,
          refreshToken: refreshToken,
          scopes: _scopes,
        ),
      );
    } on FlutterAppAuthPlatformException catch (error) {
      if (error.platformErrorDetails.error ==
          FlutterAppAuthOAuthError.invalidGrant) {
        await _clear();
        throw const AuthSessionRejected();
      }
      rethrow;
    }
    await _keep(
      accessToken: response.accessToken,
      expiry: response.accessTokenExpirationDateTime,
      refreshToken: response.refreshToken,
      idToken: response.idToken,
    );
    return _accessToken!;
  }

  @override
  Future<void> signOut() async {
    final idToken = await _storage.read(key: _idTokenKey);
    // Clear first: the person is signed out here even if the browser step is cancelled.
    await _clear();
    if (idToken != null) {
      await _appAuth.endSession(
        EndSessionRequest(
          idTokenHint: idToken,
          postLogoutRedirectUrl: _config.postLogoutRedirectUrl,
          issuer: _config.issuer,
        ),
      );
    }
  }

  Future<void> _keep({
    required String? accessToken,
    required DateTime? expiry,
    required String? refreshToken,
    required String? idToken,
  }) async {
    if (accessToken == null) {
      throw StateError('Keycloak returned no access token.');
    }
    _accessToken = accessToken;
    _accessTokenExpiry = expiry;
    // Refresh-token rotation: a new refresh token replaces the old one.
    if (refreshToken != null) {
      await _storage.write(key: _refreshTokenKey, value: refreshToken);
    }
    if (idToken != null) {
      await _storage.write(key: _idTokenKey, value: idToken);
      final parts = idToken.split('.');
      if (parts.length == 3) {
        try {
          final claims = jsonDecode(
            utf8.decode(base64Url.decode(base64Url.normalize(parts[1]))),
          ) as Map<String, dynamic>;
          if (claims['sub'] is String) {
            await _storage.write(
              key: _ownerIdKey,
              value: claims['sub'] as String,
            );
          }
        } catch (_) {
          // A malformed ID token will be rejected by the auth server on the next request.
        }
      }
    }
  }

  Future<void> _clear() async {
    _accessToken = null;
    _accessTokenExpiry = null;
    await _storage.delete(key: _refreshTokenKey);
    await _storage.delete(key: _idTokenKey);
    await _storage.delete(key: _ownerIdKey);
  }
}

class AuthRestoreUnavailable implements Exception {
  const AuthRestoreUnavailable();
}

class AuthSessionRejected implements Exception {
  const AuthSessionRejected();
}
