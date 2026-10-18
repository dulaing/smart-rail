// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appTitle => 'SmartRail';

  @override
  String get tabHome => 'Home';

  @override
  String get tabTrains => 'Trains';

  @override
  String get tabFood => 'Food';

  @override
  String get tabAccount => 'Account';

  @override
  String get navLabel => 'Main navigation';

  @override
  String get comingSoon => 'Coming soon';

  @override
  String get guest => 'Guest';

  @override
  String get guestNotice =>
      'Timetables and train times are available without an account.';

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
  String get backToGuest => 'Back to guest';

  @override
  String get language => 'Language';

  @override
  String get theme => 'Theme';

  @override
  String get themeSystem => 'System';

  @override
  String get themeLight => 'Light';

  @override
  String get themeDark => 'Dark';

  @override
  String get preferencesNote => 'Saved on this device only.';

  @override
  String get statusSample => 'Status sample';
}
