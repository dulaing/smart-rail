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
import 'trains/train_api.dart';
import 'trains/train_cache.dart';
import 'trains/train_controller.dart';

void main() {
  const config = AuthConfig();
  runApp(
    MainApp(
      auth: KeycloakAuthService(config),
      accounts: HttpAccountApi(config.apiBaseUrl),
      preferences: SharedPreferencesStore(),
      trains: TrainController(
        api: PassengerTrainApi(const AuthConfig().apiBaseUrl),
        cache: SharedPreferencesTrainCache(),
      ),
    ),
  );
}

class MainApp extends StatefulWidget {
  const MainApp({
    super.key,
    required this.auth,
    required this.accounts,
    required this.preferences,
    this.trains,
  });

  final AuthService auth;
  final AccountApi accounts;
  final PreferencesStore preferences;
  final TrainController? trains;

  @override
  State<MainApp> createState() => _MainAppState();
}

class _MainAppState extends State<MainApp> {
  late final AccountController _account = AccountController(
    auth: widget.auth,
    accounts: widget.accounts,
  );
  late final PreferencesController _preferences = PreferencesController(
    widget.preferences,
  );
  late final TrainController _trains =
      widget.trains ??
      TrainController(
        api: PassengerTrainApi(const AuthConfig().apiBaseUrl),
        cache: SharedPreferencesTrainCache(),
      );

  @override
  void initState() {
    super.initState();
    // Both run in the background: the public tabs open at once, as a guest on the device theme.
    unawaited(_preferences.load());
    unawaited(_account.restore());
    unawaited(_trains.initialize());
  }

  @override
  void dispose() {
    _account.dispose();
    _preferences.dispose();
    _trains.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: _preferences,
      builder: (context, _) {
        final locale =
            _preferences.locale ?? ui.PlatformDispatcher.instance.locale;
        return MaterialApp(
          onGenerateTitle: (context) => AppLocalizations.of(context).appTitle,
          theme: srTheme(SrProfile.passenger, Brightness.light, locale: locale),
          darkTheme: srTheme(
            SrProfile.passenger,
            Brightness.dark,
            locale: locale,
          ),
          themeMode: _preferences.themeMode,
          locale: _preferences.locale,
          supportedLocales: AppLocalizations.supportedLocales,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          home: PassengerShell(
            account: _account,
            preferences: _preferences,
            trains: _trains,
          ),
        );
      },
    );
  }
}
