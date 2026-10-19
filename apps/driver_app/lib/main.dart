import 'dart:async';

import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'account/account_controller.dart';
import 'auth/account_api.dart';
import 'auth/auth_service.dart';
import 'gate.dart';
import 'l10n/app_localizations.dart';
import 'journeys/briefing_api.dart';
import 'journeys/briefing_cache.dart';
import 'journeys/report_drafts.dart';
import 'journeys/report_api.dart';

void main() {
  const config = AuthConfig();
  final auth = KeycloakAuthService(config);
  runApp(
    MainApp(
      auth: auth,
      accounts: HttpAccountApi(config.apiBaseUrl),
      journeyApi: DriverBriefingApi(config.apiBaseUrl, auth),
      reportApi: DriverReportApi(config.apiBaseUrl, auth),
      clearSavedData: clearDriverLocalData,
      hasSavedData: (ownerId) => BriefingCache().hasSaved(ownerId),
    ),
  );
}

class MainApp extends StatefulWidget {
  const MainApp({
    super.key,
    required this.auth,
    required this.accounts,
    this.journeyApi,
    this.reportApi,
    this.clearSavedData,
    this.hasSavedData,
  });

  final AuthService auth;
  final AccountApi accounts;
  final DriverBriefingApi? journeyApi;
  final DriverReportApi? reportApi;
  final Future<void> Function()? clearSavedData;
  final Future<bool> Function(String ownerId)? hasSavedData;

  @override
  State<MainApp> createState() => _MainAppState();
}

class _MainAppState extends State<MainApp> {
  late final AccountController _account = AccountController(
    auth: widget.auth,
    accounts: widget.accounts,
    clearSavedData: widget.clearSavedData,
    hasSavedData: widget.hasSavedData,
  );

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
      home: DriverGate(
        controller: _account,
        journeyApi: widget.journeyApi,
        reportApi: widget.reportApi,
      ),
    );
  }
}

Future<void> clearDriverLocalData() async {
  try {
    await BriefingCache().clear();
  } catch (error) {
    debugPrint('Could not clear saved driver data: ${error.runtimeType}');
  }
  try {
    await ReportDraftStore().clearAll();
  } catch (error) {
    debugPrint('Could not clear saved report drafts: ${error.runtimeType}');
  }
}
