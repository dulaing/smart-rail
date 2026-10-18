import 'package:flutter/foundation.dart';

import '../auth/account_api.dart';
import '../auth/auth_service.dart';

/// Drivers must sign in: there is no guest mode. This holds the sign-in state above the screens.
/// While the session is being checked the app stays protected and shows nothing but a wait state.
class AccountController extends ChangeNotifier {
  AccountController({required this.auth, required this.accounts});

  final AuthService auth;
  final AccountApi accounts;

  AccountResult? _result;
  bool _checking = true;
  bool _signInFailed = false;
  bool _busy = false;
  bool _disposed = false;

  /// Null while signed out.
  AccountResult? get result => _result;

  /// True until the first session check finishes.
  bool get checking => _checking;
  bool get signInFailed => _signInFailed;
  bool get busy => _busy;

  Future<void> restore() async {
    try {
      if (await auth.restore()) await _loadAccount();
    } catch (_) {
      // No usable session: the person signs in again.
    }
    _checking = false;
    _notify();
  }

  Future<void> signIn() async {
    _busy = true;
    _signInFailed = false;
    _notify();
    try {
      await auth.signIn();
      await _loadAccount();
    } catch (_) {
      _signInFailed = true;
    } finally {
      _busy = false;
      _notify();
    }
  }

  /// Asks for the account again after a connection failure.
  Future<void> retry() async {
    _busy = true;
    _notify();
    try {
      await _loadAccount();
    } finally {
      _busy = false;
      _notify();
    }
  }

  Future<void> signOut() async {
    _busy = true;
    _notify();
    try {
      await auth.signOut();
    } catch (_) {
      // Local tokens are already cleared; the browser step may have been cancelled.
    }
    _result = null;
    _signInFailed = false;
    _busy = false;
    _notify();
  }

  Future<void> _loadAccount() async {
    final String token;
    try {
      token = await auth.accessToken();
    } catch (_) {
      // The stored session can no longer be refreshed.
      _result = const AccountExpired();
      _notify();
      return;
    }
    _result = await accounts.fetchMe(token);
    _notify();
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    super.dispose();
  }
}
