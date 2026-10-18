import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'account/account_controller.dart';
import 'auth/account_api.dart';
import 'l10n/app_localizations.dart';
import 'shell.dart';

/// Drivers must sign in. While the session is checked, nothing but a wait state is shown; after
/// that the person sees sign-in, an honest problem screen, or the driver shell.
class DriverGate extends StatelessWidget {
  const DriverGate({super.key, required this.controller});

  final AccountController controller;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: controller,
      builder: (context, _) {
        final t = AppLocalizations.of(context);
        if (controller.checking) {
          return _Frame(
            title: t.appTitle,
            children: [
              Semantics(liveRegion: true, child: Text(t.checkingSession)),
            ],
          );
        }
        final busy = controller.busy;
        return switch (controller.result) {
          null => _Frame(
            title: t.appTitle,
            children: [
              Text(t.signInPrompt),
              if (controller.signInFailed)
                Text(t.signInFailed, key: const Key('notice'), semanticsLabel: t.signInFailed),
              _Action(label: t.signIn, primary: true, onPressed: busy ? null : controller.signIn),
            ],
          ),
          AccountFound(:final account) => DriverShell(account: account, controller: controller),
          AccountMissing() => _Frame(
            title: t.appTitle,
            children: [
              Text(t.noAccount, key: const Key('account-message')),
              _Action(label: t.signOut, onPressed: busy ? null : controller.signOut),
            ],
          ),
          AccountExpired() => _Frame(
            title: t.appTitle,
            children: [
              Text(t.sessionExpired, key: const Key('account-message')),
              _Action(label: t.signIn, primary: true, onPressed: busy ? null : controller.signIn),
            ],
          ),
          AccountFailed(:final message) => _Frame(
            title: t.appTitle,
            children: [
              // A connection or server problem: retry is offered, and it is never called a denial.
              Text(message, key: const Key('account-message')),
              _Action(label: t.retry, primary: true, onPressed: busy ? null : controller.retry),
              _Action(label: t.signOut, onPressed: busy ? null : controller.signOut),
            ],
          ),
        };
      },
    );
  }
}

class _Frame extends StatelessWidget {
  const _Frame({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[24]!;
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: EdgeInsets.all(gap),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 560),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Semantics(header: true, child: Text(title, style: theme.text('title'))),
                  for (final child in children) ...[SizedBox(height: gap / 2), child],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Action extends StatelessWidget {
  const _Action({required this.label, required this.onPressed, this.primary = false});

  final String label;
  final VoidCallback? onPressed;
  final bool primary;

  @override
  Widget build(BuildContext context) {
    final child = Text(label);
    return Align(
      alignment: AlignmentDirectional.centerStart,
      child: primary
          ? FilledButton(onPressed: onPressed, child: child)
          : OutlinedButton(onPressed: onPressed, child: child),
    );
  }
}
