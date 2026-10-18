import 'dart:async';

import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'account/account_controller.dart';
import 'auth/account_api.dart';
import 'auth/auth_service.dart';
import 'gate.dart';
import 'l10n/app_localizations.dart';

void main() {
  const config = AuthConfig();
  runApp(
    MainApp(
      auth: KeycloakAuthService(config),
      accounts: HttpAccountApi(config.apiBaseUrl),
    ),
  );
}

class MainApp extends StatefulWidget {
  const MainApp({super.key, required this.auth, required this.accounts});

  final AuthService auth;
  final AccountApi accounts;

  @override
  State<MainApp> createState() => _MainAppState();
}

class _MainAppState extends State<MainApp> {
  late final AccountController _account = AccountController(auth: widget.auth, accounts: widget.accounts);

  @override
  void initState() {
    super.initState();
    unawaited(_account.restore());
  }

  @override
  void dispose() {
    _account.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      onGenerateTitle: (context) => AppLocalizations.of(context).appTitle,
      // The driver app has one light theme with large text and 56 dp targets; no theme picker.
      theme: srTheme(SrProfile.driver, Brightness.light),
      themeMode: ThemeMode.light,
      supportedLocales: AppLocalizations.supportedLocales,
      localizationsDelegates: AppLocalizations.localizationsDelegates,
      home: DriverGate(controller: _account),
    );
  }
}
