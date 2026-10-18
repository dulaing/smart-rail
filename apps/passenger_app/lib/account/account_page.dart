import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../auth/account_api.dart';
import '../l10n/app_localizations.dart';
import '../preferences.dart';
import 'account_controller.dart';

const _languageNames = {'en': 'English', 'si': 'සිංහල', 'ta': 'தமிழ்'};

/// Account tab: optional sign-in, then device preferences. Everything public stays reachable
/// from the other tabs whether or not this tab is signed in.
class AccountPage extends StatelessWidget {
  const AccountPage({super.key, required this.controller, required this.preferences});

  final AccountController controller;
  final PreferencesController preferences;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;

    return ListenableBuilder(
      listenable: Listenable.merge([controller, preferences]),
      builder: (context, _) {
        final result = controller.result;
        return ListView(
          padding: EdgeInsets.all(gap * 1.5),
          children: [
            switch (result) {
              AccountFound(:final account) => _signedIn(context, account),
              AccountMissing() => _message(context, t.noAccount, t.backToGuest),
              AccountFailed(:final message) => _message(context, message, t.backToGuest),
              null => _guest(context),
            },
            SizedBox(height: gap * 2),
            _preferences(context),
            if (kDebugMode) ...[
              SizedBox(height: gap),
              Align(
                alignment: AlignmentDirectional.centerStart,
                child: TextButton(
                  onPressed: () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => const StatusSample(),
                    ),
                  ),
                  child: Text(t.statusSample),
                ),
              ),
            ],
          ],
        );
      },
    );
  }

  Widget _guest(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Semantics(header: true, child: Text(t.guest, style: theme.text('title'))),
        SizedBox(height: gap / 2),
        Text(t.guestNotice, style: theme.text('body', color: theme.palette.textSecondary)),
        if (controller.signInFailed) ...[
          SizedBox(height: gap / 2),
          Text(
            t.signInFailed,
            key: const Key('notice'),
            style: theme.text('body', color: theme.palette.statusDangerInk),
          ),
        ],
        SizedBox(height: gap),
        FilledButton(onPressed: controller.busy ? null : controller.signIn, child: Text(t.signIn)),
      ],
    );
  }

  Widget _signedIn(BuildContext context, Account account) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Semantics(header: true, child: Text(account.displayName, style: theme.text('title'))),
        SizedBox(height: gap / 2),
        Text(account.roles.join(', '), style: theme.text('body', color: theme.palette.textSecondary)),
        SizedBox(height: gap),
        OutlinedButton(onPressed: controller.busy ? null : controller.signOut, child: Text(t.signOut)),
      ],
    );
  }

  Widget _message(BuildContext context, String text, String action) {
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(text, style: theme.text('body'), key: const Key('account-message')),
        SizedBox(height: gap),
        OutlinedButton(onPressed: controller.busy ? null : controller.signOut, child: Text(action)),
      ],
    );
  }

  Widget _preferences(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    final currentLanguage = preferences.locale?.languageCode ?? Localizations.localeOf(context).languageCode;

    Widget chip({required String label, required bool selected, required VoidCallback onTap, Key? key}) {
      final ink = selected ? theme.palette.navActiveInk : theme.palette.textPrimary;
      return ChoiceChip(
        key: key,
        label: Text(label, style: theme.text('body', color: ink)),
        selected: selected,
        showCheckmark: false,
        selectedColor: theme.palette.navActive,
        backgroundColor: theme.palette.bgSurface,
        side: BorderSide(color: selected ? theme.palette.navActive : theme.palette.textTertiary, width: 1.5),
        materialTapTargetSize: MaterialTapTargetSize.padded,
        padding: EdgeInsets.symmetric(horizontal: gap / 2, vertical: gap / 2),
        onSelected: (_) => onTap(),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Semantics(header: true, child: Text(t.language, style: theme.text('heading'))),
        SizedBox(height: gap / 2),
        Wrap(
          spacing: gap / 2,
          runSpacing: gap / 2,
          children: [
            for (final code in supportedLanguageCodes)
              chip(
                key: Key('language-$code'),
                label: _languageNames[code]!,
                selected: currentLanguage == code,
                onTap: () => preferences.setLanguageCode(code),
              ),
          ],
        ),
        SizedBox(height: gap * 1.5),
        Semantics(header: true, child: Text(t.theme, style: theme.text('heading'))),
        SizedBox(height: gap / 2),
        Wrap(
          spacing: gap / 2,
          runSpacing: gap / 2,
          children: [
            for (final (mode, label) in [
              (ThemeMode.system, t.themeSystem),
              (ThemeMode.light, t.themeLight),
              (ThemeMode.dark, t.themeDark),
            ])
              chip(
                key: Key('theme-${mode.name}'),
                label: label,
                selected: preferences.themeMode == mode,
                onTap: () => preferences.setThemeMode(mode),
              ),
          ],
        ),
        SizedBox(height: gap / 2),
        Text(t.preferencesNote, style: theme.text('caption', color: theme.palette.textSecondary)),
      ],
    );
  }
}
