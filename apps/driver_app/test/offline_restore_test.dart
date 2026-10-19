import 'package:driver_app/account/account_controller.dart';
import 'package:driver_app/auth/account_api.dart';
import 'package:driver_app/auth/auth_service.dart';
import 'package:flutter_test/flutter_test.dart';

class _OfflineAuth implements AuthService {
  @override
  Future<bool> restore() async => throw const AuthRestoreUnavailable();

  @override
  Future<String?> ownerId() async => 'owner-1';

  @override
  Future<void> signIn() async {}

  @override
  Future<String> accessToken() async =>
      throw StateError('offline access blocked');

  @override
  Future<void> signOut() async {}
}

class _Accounts implements AccountApi {
  int calls = 0;

  @override
  Future<AccountResult> fetchMe(String accessToken) async {
    calls++;
    return const AccountMissing();
  }
}

void main() {
  test('offline restore opens only an owner with a saved briefing', () async {
    final accounts = _Accounts();
    final controller = AccountController(
      auth: _OfflineAuth(),
      accounts: accounts,
      hasSavedData: (ownerId) async => ownerId == 'owner-1',
    );

    await controller.restore();

    expect(controller.result, isA<AccountOffline>());
    expect((controller.result as AccountOffline).ownerId, 'owner-1');
    expect(accounts.calls, 0);
    controller.dispose();
  });

  test(
    'offline restore without a saved briefing does not create an offline shell',
    () async {
      final controller = AccountController(
        auth: _OfflineAuth(),
        accounts: _Accounts(),
        hasSavedData: (_) async => false,
      );

      await controller.restore();

      expect(controller.result, isA<AccountFailed>());
      expect(controller.checking, isFalse);
      controller.dispose();
    },
  );
}
