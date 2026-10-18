import 'dart:async';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'account/account_controller.dart';
import 'auth/account_api.dart';
import 'auth/auth_service.dart';
import 'l10n/app_localizations.dart';
import 'preferences.dart';
import 'shell.dart';

void main() {
  const config = AuthConfig();
  runApp(
    MainApp(
      auth: KeycloakAuthService(config),
      accounts: HttpAccountApi(config.apiBaseUrl),
      preferences: SharedPreferencesStore(),
    ),
  );
}

class MainApp extends StatefulWidget {
  const MainApp({super.key, required this.auth, required this.accounts, required this.preferences});

  final AuthService auth;
  final AccountApi accounts;
  final PreferencesStore preferences;

  @override
  State<MainApp> createState() => _MainAppState();
}

class _MainAppState extends State<MainApp> {
  late final AccountController _account = AccountController(auth: widget.auth, accounts: widget.accounts);
  late final PreferencesController _preferences = PreferencesController(widget.preferences);

  @override
  void initState() {
    super.initState();
    // Both run in the background: the public tabs open at once, as a guest on the device theme.
    unawaited(_preferences.load());
    unawaited(_account.restore());
  }

  @override
  void dispose() {
    _account.dispose();
    _preferences.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: _preferences,
      builder: (context, _) {
        final locale = _preferences.locale ?? ui.PlatformDispatcher.instance.locale;
        return MaterialApp(
          onGenerateTitle: (context) => AppLocalizations.of(context).appTitle,
          theme: srTheme(SrProfile.passenger, Brightness.light, locale: locale),
          darkTheme: srTheme(SrProfile.passenger, Brightness.dark, locale: locale),
          themeMode: _preferences.themeMode,
          locale: _preferences.locale,
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          home: PassengerShell(account: _account, preferences: _preferences),
        );
      },
    );
  }
}
