import 'dart:async';
import 'dart:convert';

import 'package:driver_app/auth/account_api.dart';
import 'package:driver_app/auth/auth_service.dart';
import 'package:driver_app/journeys/briefing_api.dart';
import 'package:driver_app/main.dart';
import 'package:flutter/material.dart';
import 'dart:ui' show Tristate;
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

class FakeAuth implements AuthService {
  FakeAuth({this.signInFails = false, this.restoreGate, this.restores = false});

  final bool signInFails;

  /// When set, `restore()` waits for it, like a slow session check.
  final Completer<bool>? restoreGate;

  /// Whether a stored session exists to restore.
  final bool restores;
  bool signedIn = false;
  int signOuts = 0;

  @override
  Future<bool> restore() async => restoreGate?.future ?? restores;

  @override
  Future<void> signIn() async {
    if (signInFails) throw StateError('cancelled');
    signedIn = true;
  }

  @override
  Future<String> accessToken() async => 'access-token';

  @override
  Future<String?> ownerId() async => signedIn ? 'driver-subject' : null;

  @override
  Future<void> signOut() async {
    signedIn = false;
    signOuts++;
  }
}

class FakeAccounts implements AccountApi {
  FakeAccounts(this.result);

  AccountResult result;
  String? tokenSeen;
  int calls = 0;

  @override
  Future<AccountResult> fetchMe(String accessToken) async {
    tokenSeen = accessToken;
    calls++;
    return result;
  }
}

const _driver = Account(
  displayName: 'Demo Driver',
  roles: ['DRIVER'],
  permissions: ['driver.briefing.read', 'driver.reports.create', 'driver.reports.read'],
);

const _controller = Account(
  displayName: 'Demo Controller',
  roles: ['CONTROLLER'],
  permissions: ['changes.publish', 'incidents.read'],
);

const _journeyId = '11111111-1111-4111-8111-111111111111';

String _assignedJourneys() => jsonEncode({
  'serviceDate': '2026-10-18',
  'journeys': [
    {
      'journeyId': _journeyId,
      'serviceDate': '2026-10-18',
      'trainNumber': 'SIM-1001',
      'timetableLabel': 'Morning express',
      'originName': 'Gampaha',
      'scheduledDeparture': '2026-10-18T02:00:00Z',
      'destinationName': 'Colombo Fort',
      'scheduledArrival': '2026-10-18T03:00:00Z',
      'availability': 'AS_SCHEDULED',
    },
  ],
});

Future<void> _pump(WidgetTester tester, FakeAuth auth, FakeAccounts accounts) async {
  await tester.pumpWidget(MainApp(auth: auth, accounts: accounts));
  await tester.pumpAndSettle();
}

Future<void> _signIn(WidgetTester tester) async {
  await tester.tap(find.text('Sign in'));
  await tester.pumpAndSettle();
}

Future<void> _openAccount(WidgetTester tester) async {
  await tester.tap(find.byKey(const Key('tab-account')));
  await tester.pumpAndSettle();
}

void _phone(WidgetTester tester) {
  tester.view.physicalSize = const Size(360 * 3, 740 * 3);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
}

void _tablet(WidgetTester tester) {
  tester.view.physicalSize = const Size(1280, 800);
  tester.view.devicePixelRatio = 1;
  addTearDown(tester.view.reset);
}

void main() {
  testWidgets('asks the driver to sign in', (tester) async {
    await _pump(tester, FakeAuth(), FakeAccounts(const AccountFound(_driver)));

    expect(find.text('Sign in to see your journeys.'), findsOneWidget);
  });

  testWidgets('the app stays protected while the session is checked', (tester) async {
    final gate = Completer<bool>();
    await tester.pumpWidget(
      MainApp(auth: FakeAuth(restoreGate: gate), accounts: FakeAccounts(const AccountFound(_driver))),
    );
    await tester.pump();

    expect(find.text('Checking your session…'), findsOneWidget);
    expect(find.text('Sign in'), findsNothing);
    expect(find.byKey(const Key('tab-journeys')), findsNothing);

    gate.complete(false);
    await tester.pumpAndSettle();
    expect(find.text('Sign in to see your journeys.'), findsOneWidget);
  });

  testWidgets('a restored session opens the shell without asking again', (tester) async {
    _tablet(tester);
    final accounts = FakeAccounts(const AccountFound(_driver));
    await _pump(tester, FakeAuth(restores: true), accounts);

    expect(find.byKey(const Key('tab-journeys')), findsOneWidget);
    expect(find.text('Sign in'), findsNothing);
  });

  testWidgets('signing in shows the account and sends the access token', (tester) async {
    final accounts = FakeAccounts(const AccountFound(_driver));
    await _pump(tester, FakeAuth(), accounts);

    await _signIn(tester);
    await _openAccount(tester);

    expect(find.text('Demo Driver'), findsOneWidget);
    expect(find.text('Driver'), findsOneWidget);
    expect(accounts.tokenSeen, 'access-token');
  });

  testWidgets('signing out clears the session', (tester) async {
    final auth = FakeAuth();
    await _pump(tester, auth, FakeAccounts(const AccountFound(_driver)));
    await _signIn(tester);
    await _openAccount(tester);

    await tester.tap(find.text('Sign out'));
    await tester.pumpAndSettle();

    expect(auth.signedIn, isFalse);
    expect(auth.signOuts, 1);
    expect(find.text('Sign in to see your journeys.'), findsOneWidget);
  });

  testWidgets('a cancelled sign-in shows a notice', (tester) async {
    await _pump(tester, FakeAuth(signInFails: true), FakeAccounts(const AccountFound(_driver)));

    await _signIn(tester);

    expect(find.byKey(const Key('notice')), findsOneWidget);
    expect(find.text('Sign in to see your journeys.'), findsOneWidget);
  });

  group('problems are not confused with each other', () {
    testWidgets('a missing or disabled account can only sign out', (tester) async {
      await _pump(tester, FakeAuth(), FakeAccounts(const AccountMissing()));
      await _signIn(tester);

      expect(find.textContaining('no SmartRail account'), findsOneWidget);
      expect(find.text('Try again'), findsNothing);
      await tester.tap(find.text('Sign out'));
      await tester.pumpAndSettle();
      expect(find.text('Sign in to see your journeys.'), findsOneWidget);
    });

    testWidgets('an expired session offers sign-in again', (tester) async {
      await _pump(tester, FakeAuth(), FakeAccounts(const AccountExpired()));
      await _signIn(tester);

      expect(find.text('Your session has expired. Sign in again.'), findsOneWidget);
      expect(find.text('Sign in'), findsOneWidget);
    });

    testWidgets('a connection failure offers retry and is not called a permission denial', (tester) async {
      _tablet(tester);
      final accounts = FakeAccounts(const AccountFailed('Could not reach SmartRail.'));
      await _pump(tester, FakeAuth(), accounts);
      await _signIn(tester);

      expect(find.text('Could not reach SmartRail.'), findsOneWidget);
      expect(find.textContaining('no SmartRail account'), findsNothing);
      expect(find.textContaining('does not have access'), findsNothing);

      accounts.result = const AccountFound(_driver);
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(accounts.calls, 2);
      expect(find.byKey(const Key('tab-journeys')), findsOneWidget);
    });
  });

  group('driver shell', () {
    testWidgets('shows only the destinations this account may use, with honest empty content', (tester) async {
      _tablet(tester);
      await _pump(tester, FakeAuth(), FakeAccounts(const AccountFound(_driver)));
      await _signIn(tester);

      for (final key in ['journeys', 'updates', 'reports', 'account']) {
        expect(find.byKey(Key('tab-$key')), findsOneWidget, reason: key);
      }
      expect(find.text('Not available yet'), findsOneWidget);
    });

    testWidgets('the shell renders the assigned journeys list, not a blank pane', (tester) async {
      _phone(tester);
      final api = DriverBriefingApi(
        'https://api.test',
        FakeAuth(restores: true),
        client: MockClient((_) async => http.Response(_assignedJourneys(), 200)),
      );
      await tester.pumpWidget(
        MainApp(
          auth: FakeAuth(restores: true),
          accounts: FakeAccounts(const AccountFound(_driver)),
          journeyApi: api,
        ),
      );
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
      expect(find.byKey(const Key('driver-journeys-list')), findsOneWidget);
      expect(find.byKey(const Key('journey-$_journeyId')), findsOneWidget);
      expect(find.text('SIM-1001 · Morning express'), findsOneWidget);
    });

    testWidgets('a staff account without driver permissions gets Account only and an explanation', (tester) async {
      _phone(tester);
      await _pump(tester, FakeAuth(), FakeAccounts(const AccountFound(_controller)));
      await _signIn(tester);

      expect(find.byKey(const Key('tab-journeys')), findsNothing);
      expect(find.byKey(const Key('tab-reports')), findsNothing);
      expect(find.byKey(const Key('tab-account')), findsOneWidget);
      expect(find.byKey(const Key('no-driver-access')), findsOneWidget);
    });

    for (final (name, setup) in <(String, void Function(WidgetTester))>[('phone', _phone), ('tablet', _tablet)]) {
      testWidgets('$name layout: 56 dp targets, 20sp body text, nothing clipped', (tester) async {
        setup(tester);
        await _pump(tester, FakeAuth(), FakeAccounts(const AccountFound(_driver)));
        await _signIn(tester);

        expect(tester.takeException(), isNull);
        // The tablet rail holds every destination; the phone frame keeps the Account button.
        for (final key in name == 'phone' ? ['account'] : ['journeys', 'updates', 'reports', 'account']) {
          final rect = tester.getRect(find.byKey(Key('tab-$key')));
          expect(rect.height, greaterThanOrEqualTo(56), reason: '$key height');
          expect(rect.width, greaterThanOrEqualTo(56), reason: '$key width');
        }
        final button = tester.getSize(find.byType(FilledButton).evaluate().isEmpty
            ? find.byKey(const Key('tab-account'))
            : find.byType(FilledButton));
        expect(button.height, greaterThanOrEqualTo(56));

        final body = tester.widget<Text>(find.text('Not available yet'));
        expect(body.style!.fontSize, 20);
      });

      testWidgets('$name layout at 200% text has no overflow', (tester) async {
        setup(tester);
        final size = tester.view.physicalSize / tester.view.devicePixelRatio;
        await tester.pumpWidget(
          MediaQuery(
            data: MediaQueryData(size: size, textScaler: const TextScaler.linear(2)),
            child: MainApp(auth: FakeAuth(), accounts: FakeAccounts(const AccountFound(_driver))),
          ),
        );
        await tester.pumpAndSettle();
        await _signIn(tester);
        await _openAccount(tester);

        expect(tester.takeException(), isNull);
        final rect = tester.getRect(find.byKey(const Key('tab-account')));
        expect(rect.right, lessThanOrEqualTo(size.width));
        expect(rect.bottom, lessThanOrEqualTo(size.height));
      });
    }

    for (final (name, setup) in <(String, void Function(WidgetTester))>[('phone', _phone), ('tablet', _tablet)]) {
      testWidgets('$name layout meets the Flutter tap target, label and contrast guidelines', (tester) async {
        setup(tester);
        final handle = tester.ensureSemantics();
        await _pump(tester, FakeAuth(), FakeAccounts(const AccountFound(_driver)));
        await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
        await expectLater(tester, meetsGuideline(textContrastGuideline));
        await _signIn(tester);
        await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
        await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
        await expectLater(tester, meetsGuideline(textContrastGuideline));
        await _openAccount(tester);
        await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
        await expectLater(tester, meetsGuideline(textContrastGuideline));
        handle.dispose();
      });
    }

    testWidgets('times are at least 28sp', (tester) async {
      await _pump(tester, FakeAuth(), FakeAccounts(const AccountFound(_driver)));
      final theme = Theme.of(tester.element(find.byType(Scaffold).first));
      expect(theme.textTheme.displaySmall!.fontSize, 28);
    });

    testWidgets('each tab is a labelled button and the open tab is selected', (tester) async {
      _tablet(tester);
      final handle = tester.ensureSemantics();
      await _pump(tester, FakeAuth(), FakeAccounts(const AccountFound(_driver)));
      await _signIn(tester);

      final data = tester.getSemantics(find.byKey(const Key('tab-journeys'))).getSemanticsData();
      expect(data.label, 'Briefing');
      expect(data.flagsCollection.isButton, isTrue);
      expect(data.flagsCollection.isSelected, Tristate.isTrue);
      handle.dispose();
    });
  });
}
