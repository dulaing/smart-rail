import 'package:driver_app/auth/account_api.dart';
import 'package:driver_app/auth/auth_service.dart';
import 'package:driver_app/main.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

class FakeAuth implements AuthService {
  FakeAuth({this.signInFails = false});

  final bool signInFails;
  bool signedIn = false;
  int signOuts = 0;

  @override
  Future<bool> restore() async => false;

  @override
  Future<void> signIn() async {
    if (signInFails) throw StateError('cancelled');
    signedIn = true;
  }

  @override
  Future<String> accessToken() async => 'access-token';

  @override
  Future<void> signOut() async {
    signedIn = false;
    signOuts++;
  }
}

class FakeAccounts implements AccountApi {
  FakeAccounts(this.result);

  final AccountResult result;
  String? tokenSeen;

  @override
  Future<AccountResult> fetchMe(String accessToken) async {
    tokenSeen = accessToken;
    return result;
  }
}

const _driver = Account(
  displayName: 'Demo Driver',
  roles: ['DRIVER'],
  permissions: ['driver.briefing.read'],
);

Future<void> _pump(WidgetTester tester, FakeAuth auth, FakeAccounts accounts) async {
  await tester.pumpWidget(MainApp(auth: auth, accounts: accounts));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('asks the driver to sign in', (tester) async {
    await _pump(tester, FakeAuth(), FakeAccounts(const AccountFound(_driver)));

    expect(find.text('Sign in to see your journeys.'), findsOneWidget);
  });

  testWidgets('signing in shows the account and sends the access token', (tester) async {
    final accounts = FakeAccounts(const AccountFound(_driver));
    await _pump(tester, FakeAuth(), accounts);

    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Demo Driver'), findsOneWidget);
    expect(find.text('DRIVER'), findsOneWidget);
    expect(accounts.tokenSeen, 'access-token');
  });

  testWidgets('signing out clears the session', (tester) async {
    final auth = FakeAuth();
    await _pump(tester, auth, FakeAccounts(const AccountFound(_driver)));
    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Sign out'));
    await tester.pumpAndSettle();

    expect(auth.signedIn, isFalse);
    expect(auth.signOuts, 1);
    expect(find.text('Sign in to see your journeys.'), findsOneWidget);
  });

  testWidgets('a cancelled sign-in shows a notice', (tester) async {
    await _pump(
      tester,
      FakeAuth(signInFails: true),
      FakeAccounts(const AccountFound(_driver)),
    );

    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('notice')), findsOneWidget);
  });
}
