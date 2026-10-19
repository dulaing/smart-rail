import 'package:flutter/foundation.dart';

import '../auth/account_api.dart';
import '../auth/auth_service.dart';

/// Drivers must sign in: there is no guest mode. This holds the sign-in state above the screens.
/// While the session is being checked the app stays protected and shows nothing but a wait state.
class AccountController extends ChangeNotifier {
  AccountController({
    required this.auth,
    required this.accounts,
    this.clearSavedData,
    this.hasSavedData,
  });

  final AuthService auth;
  final AccountApi accounts;
  final Future<void> Function()? clearSavedData;
  final Future<bool> Function(String ownerId)? hasSavedData;

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
    String? ownerBeforeRestore;
    try {
      ownerBeforeRestore = await auth.ownerId();
      if (await auth.restore()) {
        await _loadAccount();
      } else if (ownerBeforeRestore != null) {
        await clearSavedData?.call();
      }
    } on AuthRestoreUnavailable {
      await _showOfflineIfOwned();
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
      final previousOwner = await auth.ownerId();
      await auth.signIn();
      final nextOwner = await auth.ownerId();
      if (previousOwner != null && nextOwner != previousOwner) {
        await clearSavedData?.call();
      }
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
    await clearSavedData?.call();
    _result = null;
    _signInFailed = false;
    _busy = false;
    _notify();
  }

  Future<void> _loadAccount() async {
    final String token;
    try {
      token = await auth.accessToken();
    } on AuthSessionRejected {
      await clearSavedData?.call();
      _result = const AccountExpired();
      _notify();
      return;
    } catch (_) {
      // The stored session can no longer be refreshed.
      _result = const AccountExpired();
      _notify();
      return;
    }
    final result = await accounts.fetchMe(token);
    if (result is AccountFailed &&
        (result.statusCode == null || result.statusCode! >= 500)) {
      await _showOfflineIfOwned();
      return;
    }
    if (result is AccountExpired || result is AccountMissing) {
      await clearSavedData?.call();
    }
    _result = result;
    _notify();
  }

  Future<void> _showOfflineIfOwned() async {
    final ownerId = await auth.ownerId();
    if (ownerId != null &&
        await (hasSavedData?.call(ownerId) ?? Future.value(false))) {
      _result = AccountOffline(ownerId);
    } else {
      _result = const AccountFailed('Could not reach SmartRail.');
    }
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
