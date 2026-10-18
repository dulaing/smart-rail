import 'package:flutter/foundation.dart';

import '../auth/account_api.dart';
import '../auth/auth_service.dart';

/// A passenger starts as a guest. Signing in is optional, and cancelling or signing out returns
/// to guest mode, so public information never depends on an account. This holds the account state
/// above the tabs so it survives switching between them.
class AccountController extends ChangeNotifier {
  AccountController({required this.auth, required this.accounts});

  final AuthService auth;
  final AccountApi accounts;

  AccountResult? _result;
  bool _signInFailed = false;
  bool _busy = false;
  bool _disposed = false;

  /// Null while a guest.
  AccountResult? get result => _result;
  bool get signInFailed => _signInFailed;
  bool get busy => _busy;

  /// Reopens a previous session in the background. Public tabs never wait for this, and a failure
  /// simply leaves the person as a guest.
  Future<void> restore() async {
    try {
      if (await auth.restore()) await _loadAccount();
    } catch (_) {
      // Stay a guest.
    }
  }

  Future<void> signIn() async {
    _busy = true;
    _signInFailed = false;
    _notify();
    try {
      await auth.signIn();
      await _loadAccount();
    } catch (_) {
      // Cancelled or failed: stay in guest mode.
      _signInFailed = true;
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
    final token = await auth.accessToken();
    final result = await accounts.fetchMe(token);
    _result = result;
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
