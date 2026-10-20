import 'dart:async';
import 'dart:ui' show Tristate;

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:passenger_app/auth/account_api.dart';
import 'package:passenger_app/auth/auth_service.dart';
import 'package:passenger_app/main.dart';
import 'package:passenger_app/preferences.dart';

class FakeAuth implements AuthService {
  FakeAuth({this.signInFails = false, this.restoreGate});

  final bool signInFails;

  /// When set, `restore()` waits for it, like a slow or unreachable sign-in service.
  final Completer<bool>? restoreGate;
  bool signedIn = false;
  int signOuts = 0;

  @override
  Future<bool> restore() async => restoreGate?.future ?? false;

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
  accountId: 'acc-1',
  displayName: 'Demo Passenger',
  roles: ['PASSENGER'],
  permissions: ['orders.manage'],
);

Future<void> _pump(
  WidgetTester tester,
  FakeAuth auth,
  FakeAccounts accounts, {
  PreferencesStore? preferences,
}) async {
  await tester.pumpWidget(
    MainApp(
      auth: auth,
      accounts: accounts,
      preferences: preferences ?? MemoryPreferencesStore(),
    ),
  );
  await tester.pumpAndSettle();
}

Future<void> _openAccount(WidgetTester tester) async {
  await tester.tap(find.byKey(const Key('tab-account')));
  await tester.pumpAndSettle();
}

Future<void> _chooseTheme(WidgetTester tester, ThemeMode mode) async {
  final choice = find.byKey(Key('theme-${mode.name}'));
  await tester.dragUntilVisible(
    choice,
    find.byKey(const Key('account-scroll')),
    const Offset(0, -400),
  );
  await tester.ensureVisible(choice);
  await tester.pumpAndSettle();
  await tester.tap(choice);
  await tester.pumpAndSettle();
}

Future<void> _scrollAccountToTop(WidgetTester tester) async {
  await tester.drag(
    find.byKey(const Key('account-scroll')),
    const Offset(0, 900),
  );
  await tester.pumpAndSettle();
}

ThemeMode _themeMode(WidgetTester tester) =>
    tester.widget<MaterialApp>(find.byType(MaterialApp)).themeMode ??
    ThemeMode.system;

void main() {
  testWidgets('starts as a guest on Trains with all four tabs in order', (
    tester,
  ) async {
    await _pump(
      tester,
      FakeAuth(),
      FakeAccounts(const AccountFound(_passenger)),
    );

    // Trains is the visible heading; Home, Trains, Food and Account are all in the bar.
    expect(find.text('Trains'), findsWidgets);
    for (final key in ['home', 'trains', 'food', 'account']) {
      expect(find.byKey(Key('tab-$key')), findsOneWidget);
    }
    final lefts = [
      for (final key in ['home', 'trains', 'food', 'account'])
        tester.getTopLeft(find.byKey(Key('tab-$key'))).dx,
    ];
    expect(lefts, [...lefts]..sort());
    expect(
      find.text('Sign in'),
      findsNothing,
      reason: 'Account is not the opening tab',
    );
  });

  testWidgets('every tab opens as a guest without waiting for sign-in', (
    tester,
  ) async {
    final gate = Completer<bool>(); // the session check never finishes
    await _pump(
      tester,
      FakeAuth(restoreGate: gate),
      FakeAccounts(const AccountFound(_passenger)),
    );

    for (final (key, title) in [
      ('home', 'Home'),
      ('food', 'Food'),
      ('trains', 'Trains'),
    ]) {
      await tester.tap(find.byKey(Key('tab-$key')));
      await tester.pump(const Duration(milliseconds: 100));
      expect(
        find.text(
          title == 'Home'
              ? 'Next train'
              : title == 'Trains'
              ? 'Trains'
              : 'Choose a train',
        ),
        title == 'Trains' ? findsWidgets : findsOneWidget,
        reason: '$title is reachable while restoring',
      );
    }
    await _openAccount(tester);
    expect(find.text('Browsing as guest'), findsOneWidget);
  });

  testWidgets('Account starts as a guest with sign-in offered', (tester) async {
    await _pump(
      tester,
      FakeAuth(),
      FakeAccounts(const AccountFound(_passenger)),
    );
    await _openAccount(tester);

    expect(find.text('Browsing as guest'), findsOneWidget);
    expect(find.text('Sign in to demo'), findsOneWidget);
  });

  testWidgets('signing in shows the account and sends the access token', (
    tester,
  ) async {
    final accounts = FakeAccounts(const AccountFound(_passenger));
    await _pump(tester, FakeAuth(), accounts);
    await _openAccount(tester);

    await tester.tap(find.text('Sign in to demo'));
    await tester.pumpAndSettle();

    expect(find.text('Demo Passenger'), findsOneWidget);
    expect(find.text('PASSENGER'), findsOneWidget);
    expect(accounts.tokenSeen, 'access-token');
  });

  testWidgets('signing out clears the session and returns to guest mode', (
    tester,
  ) async {
    final auth = FakeAuth();
    await _pump(tester, auth, FakeAccounts(const AccountFound(_passenger)));
    await _openAccount(tester);
    await tester.tap(find.text('Sign in to demo'));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Sign out'));
    await tester.pumpAndSettle();

    expect(auth.signedIn, isFalse);
    expect(auth.signOuts, 1);
    expect(find.text('Browsing as guest'), findsOneWidget);
  });

  testWidgets(
    'a cancelled sign-in stays in guest mode with a notice and public tabs still open',
    (tester) async {
      await _pump(
        tester,
        FakeAuth(signInFails: true),
        FakeAccounts(const AccountFound(_passenger)),
      );
      await _openAccount(tester);

      await tester.tap(find.text('Sign in to demo'));
      await tester.pumpAndSettle();

      expect(find.text('Browsing as guest'), findsOneWidget);
      expect(find.byKey(const Key('notice')), findsOneWidget);

      await tester.tap(find.byKey(const Key('tab-trains')));
      await tester.pumpAndSettle();
      expect(find.text('Trains'), findsWidgets);
    },
  );

  testWidgets(
    'a signed-in identity with no (or a disabled) local account can go back to guest',
    (tester) async {
      await _pump(tester, FakeAuth(), FakeAccounts(const AccountMissing()));
      await _openAccount(tester);

      await tester.tap(find.text('Sign in to demo'));
      await tester.pumpAndSettle();
      expect(find.textContaining('no SmartRail account'), findsOneWidget);

      await tester.tap(find.text('Back to guest'));
      await tester.pumpAndSettle();
      expect(find.text('Browsing as guest'), findsOneWidget);
    },
  );

  testWidgets(
    'a connection failure is shown as a message, not as a missing account',
    (tester) async {
      await _pump(
        tester,
        FakeAuth(),
        FakeAccounts(const AccountFailed('Could not reach SmartRail.')),
      );
      await _openAccount(tester);

      await tester.tap(find.text('Sign in to demo'));
      await tester.pumpAndSettle();

      expect(find.text('Could not reach SmartRail.'), findsOneWidget);
      expect(find.textContaining('no SmartRail account'), findsNothing);
    },
  );

  group('device preferences', () {
    testWidgets('follow the device until a choice is made', (tester) async {
      await _pump(
        tester,
        FakeAuth(),
        FakeAccounts(const AccountFound(_passenger)),
      );
      expect(_themeMode(tester), ThemeMode.system);
    });

    testWidgets('System, Light and Dark can each be chosen', (tester) async {
      await _pump(
        tester,
        FakeAuth(),
        FakeAccounts(const AccountFound(_passenger)),
      );
      await _openAccount(tester);

      for (final mode in [ThemeMode.dark, ThemeMode.light, ThemeMode.system]) {
        await _chooseTheme(tester, mode);
        expect(_themeMode(tester), mode);
      }
    });

    testWidgets('the choice survives a restart', (tester) async {
      final store = MemoryPreferencesStore();
      await _pump(
        tester,
        FakeAuth(),
        FakeAccounts(const AccountFound(_passenger)),
        preferences: store,
      );
      await _openAccount(tester);
      await _chooseTheme(tester, ThemeMode.dark);

      // A restart: a new app over the same stored preferences.
      await tester.pumpWidget(const SizedBox());
      await _pump(
        tester,
        FakeAuth(),
        FakeAccounts(const AccountFound(_passenger)),
        preferences: store,
      );
      expect(_themeMode(tester), ThemeMode.dark);
    });

    testWidgets('the choice survives sign-out and a switch of account', (
      tester,
    ) async {
      final store = MemoryPreferencesStore();
      final auth = FakeAuth();
      await _pump(
        tester,
        auth,
        FakeAccounts(const AccountFound(_passenger)),
        preferences: store,
      );
      await _openAccount(tester);
      await _chooseTheme(tester, ThemeMode.dark);
      await _scrollAccountToTop(tester);
      await tester.tap(find.text('Sign in to demo'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Sign out'));
      await tester.pumpAndSettle();

      expect(_themeMode(tester), ThemeMode.dark);
      expect(store.themeMode, ThemeMode.dark);
    });

    testWidgets(
      'a language choice changes the shell wording and survives restart',
      (tester) async {
        final store = MemoryPreferencesStore();
        await _pump(
          tester,
          FakeAuth(),
          FakeAccounts(const AccountFound(_passenger)),
          preferences: store,
        );
        await _openAccount(tester);

        await tester.tap(find.byKey(const Key('language-si')));
        await tester.pumpAndSettle();
        expect(find.text('අමුත්තා'), findsOneWidget); // Guest
        expect(store.languageCode, 'si');

        await tester.pumpWidget(const SizedBox());
        await _pump(
          tester,
          FakeAuth(),
          FakeAccounts(const AccountFound(_passenger)),
          preferences: store,
        );
        expect(find.text('දුම්රිය'), findsWidgets); // Trains
      },
    );
  });

  group('accessibility', () {
    for (final mode in ThemeMode.values.where((m) => m != ThemeMode.system)) {
      testWidgets(
        'meets the Flutter tap target, label and contrast guidelines (${mode.name})',
        (tester) async {
          final handle = tester.ensureSemantics();
          final store = MemoryPreferencesStore()..themeMode = mode;
          await _pump(
            tester,
            FakeAuth(),
            FakeAccounts(const AccountFound(_passenger)),
            preferences: store,
          );
          await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
          await expectLater(tester, meetsGuideline(textContrastGuideline));

          await _openAccount(tester);
          await expectLater(tester, meetsGuideline(androidTapTargetGuideline));
          await expectLater(tester, meetsGuideline(labeledTapTargetGuideline));
          await expectLater(tester, meetsGuideline(textContrastGuideline));
          handle.dispose();
        },
      );
    }

    testWidgets('each tab is a labelled button and the open tab is selected', (
      tester,
    ) async {
      final handle = tester.ensureSemantics();
      await _pump(
        tester,
        FakeAuth(),
        FakeAccounts(const AccountFound(_passenger)),
      );

      for (final (key, label, selected) in [
        ('trains', 'Trains', true),
        ('food', 'Food', false),
      ]) {
        final data = tester
            .getSemantics(find.byKey(Key('tab-$key')))
            .getSemanticsData();
        expect(data.label, label);
        expect(data.flagsCollection.isButton, isTrue, reason: label);
        expect(
          data.flagsCollection.isSelected,
          selected ? Tristate.isTrue : Tristate.isFalse,
          reason: label,
        );
        expect(
          data.flagsCollection.isInMutuallyExclusiveGroup,
          isTrue,
          reason: label,
        );
        expect(data.hasAction(SemanticsAction.tap), isTrue, reason: label);
      }
      handle.dispose();
    });

    for (final locale in ['en', 'si', 'ta']) {
      testWidgets(
        '200% text keeps the navigation and Account controls unclipped ($locale)',
        (tester) async {
          final store = MemoryPreferencesStore()..languageCode = locale;
          tester.view.physicalSize = const Size(360 * 3, 640 * 3);
          tester.view.devicePixelRatio = 3;
          addTearDown(tester.view.reset);
          await tester.pumpWidget(
            MediaQuery(
              data: const MediaQueryData(
                size: Size(360, 640),
                textScaler: TextScaler.linear(2),
              ),
              child: MainApp(
                auth: FakeAuth(),
                accounts: FakeAccounts(const AccountFound(_passenger)),
                preferences: store,
              ),
            ),
          );
          await tester.pumpAndSettle();
          await _openAccount(tester);

          expect(tester.takeException(), isNull);
          for (final key in ['home', 'trains', 'food', 'account']) {
            final rect = tester.getRect(find.byKey(Key('tab-$key')));
            expect(rect.left, greaterThanOrEqualTo(0), reason: key);
            expect(rect.right, lessThanOrEqualTo(360), reason: key);
            expect(
              rect.height,
              greaterThanOrEqualTo(48),
              reason: '$key target height',
            );
          }
          // The preferences sit below the sign-in block; scroll them into view and check them.
          await tester.scrollUntilVisible(
            find.byKey(const Key('theme-dark')),
            200,
          );
          expect(tester.takeException(), isNull);
          final chip = tester.getRect(find.byKey(const Key('theme-dark')));
          expect(chip.right, lessThanOrEqualTo(360));
          expect(chip.height, greaterThanOrEqualTo(48));
        },
      );
    }
  });
}
