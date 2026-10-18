// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appTitle => 'SmartRail Driver';

  @override
  String get checkingSession => 'Checking your session…';

  @override
  String get signInPrompt => 'Sign in to see your journeys.';

  @override
  String get signIn => 'Sign in';

  @override
  String get signOut => 'Sign out';

  @override
  String get signInFailed => 'Sign-in was cancelled or failed.';

  @override
  String get noAccount =>
      'You are signed in, but there is no SmartRail account for you.';

  @override
  String get sessionExpired => 'Your session has expired. Sign in again.';

  @override
  String get retry => 'Try again';

  @override
  String get navLabel => 'Driver navigation';

  @override
  String get tabJourneys => 'Journeys';

  @override
  String get tabUpdates => 'Updates';

  @override
  String get tabReports => 'Reports';

  @override
  String get tabAccount => 'Account';

  @override
  String get notAvailableYet => 'Not available yet';

  @override
  String get noDriverAccess =>
      'This account has no driver access. Sign out and use a driver account.';

  @override
  String get accessDenied =>
      'Your account does not have access to this screen.';

  @override
  String get statusSample => 'Status sample';
}
