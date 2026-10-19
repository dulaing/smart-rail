import 'package:flutter/material.dart';

import 'account/account_controller.dart';
import 'auth/account_api.dart';
import 'gate_view.dart';
import 'journeys/briefing_api.dart';
import 'journeys/report_api.dart';
import 'l10n/app_localizations.dart';
import 'shell.dart';

/// Drivers must sign in. While the session is checked, nothing but a wait state is shown; after
/// that the person sees sign-in, an honest problem screen, or the driver shell.
class DriverGate extends StatelessWidget {
  const DriverGate({
    super.key,
    required this.controller,
    this.journeyApi,
    this.reportApi,
  });

  final AccountController controller;
  final DriverBriefingApi? journeyApi;
  final DriverReportApi? reportApi;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: controller,
      builder: (context, _) {
        final t = AppLocalizations.of(context);
        if (controller.checking) {
          return const DriverGateView(kind: GateKind.checking);
        }
        final busy = controller.busy;
        return switch (controller.result) {
          null => DriverGateView(
            kind: GateKind.signedOut,
            busy: busy,
            signInFailed: controller.signInFailed,
            signInFailedMessage: t.signInFailed,
            onSignIn: controller.signIn,
          ),
          AccountFound(:final account) => DriverShell(
            account: account,
            controller: controller,
            journeyApi: journeyApi,
            reportApi: reportApi,
          ),
          AccountOffline(:final ownerId) => DriverShell(
            account: const Account(
              displayName: 'Saved driver',
              roles: [],
              permissions: ['driver.briefing.read'],
            ),
            controller: controller,
            journeyApi: journeyApi,
            reportApi: reportApi,
            offlineOwnerId: ownerId,
          ),
          AccountMissing() => DriverGateView(
            kind: GateKind.missing,
            message: t.noAccount,
            busy: busy,
            onSignOut: controller.signOut,
          ),
          AccountExpired() => DriverGateView(
            kind: GateKind.expired,
            busy: busy,
            signInFailed: controller.signInFailed,
            signInFailedMessage: t.signInFailed,
            onSignIn: controller.signIn,
          ),
          // A connection or server problem: retry is offered, and it is never called a denial.
          AccountFailed(:final message) => DriverGateView(
            kind: GateKind.failed,
            message: message,
            busy: busy,
            onRetry: controller.retry,
            onSignOut: controller.signOut,
          ),
        };
      },
    );
  }
}
