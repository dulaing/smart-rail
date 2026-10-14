import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/auth/account_api.dart';
import 'package:passenger_app/auth/auth_service.dart';
import 'package:passenger_app/main.dart';

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

const _passenger = Account(
  displayName: 'Demo Passenger',
  roles: ['PASSENGER'],
  permissions: ['orders.manage'],
);

Future<void> _pump(WidgetTester tester, FakeAuth auth, FakeAccounts accounts) async {
  await tester.pumpWidget(MainApp(auth: auth, accounts: accounts));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('starts as a guest with no sign-in required', (tester) async {
    await _pump(tester, FakeAuth(), FakeAccounts(const AccountFound(_passenger)));

    expect(find.text('Guest'), findsOneWidget);
    expect(find.text('Sign in'), findsOneWidget);
  });

  testWidgets('signing in shows the account and sends the access token', (tester) async {
    final accounts = FakeAccounts(const AccountFound(_passenger));
    await _pump(tester, FakeAuth(), accounts);

    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Demo Passenger'), findsOneWidget);
    expect(find.text('PASSENGER'), findsOneWidget);
    expect(accounts.tokenSeen, 'access-token');
  });

  testWidgets('signing out clears the session and returns to guest mode', (tester) async {
    final auth = FakeAuth();
    await _pump(tester, auth, FakeAccounts(const AccountFound(_passenger)));
    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Sign out'));
    await tester.pumpAndSettle();

    expect(auth.signedIn, isFalse);
    expect(auth.signOuts, 1);
    expect(find.text('Guest'), findsOneWidget);
  });

  testWidgets('a cancelled sign-in stays in guest mode with a notice', (tester) async {
    await _pump(
      tester,
      FakeAuth(signInFails: true),
      FakeAccounts(const AccountFound(_passenger)),
    );

    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();

    expect(find.text('Guest'), findsOneWidget);
    expect(find.byKey(const Key('notice')), findsOneWidget);
  });

  testWidgets('a signed-in identity with no local account can go back to guest', (tester) async {
    await _pump(tester, FakeAuth(), FakeAccounts(const AccountMissing()));

    await tester.tap(find.text('Sign in'));
    await tester.pumpAndSettle();
    expect(find.textContaining('no SmartRail account'), findsOneWidget);

    await tester.tap(find.text('Back to guest'));
    await tester.pumpAndSettle();
    expect(find.text('Guest'), findsOneWidget);
  });
}
