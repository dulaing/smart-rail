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
  const AccountPage({
    super.key,
    required this.controller,
    required this.preferences,
  });

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
          key: const Key('account-scroll'),
          padding: const EdgeInsets.fromLTRB(22, 12, 22, 20),
          children: [
            _pageHeader(context),
            const SizedBox(height: 18),
            switch (result) {
              AccountFound(:final account) => _signedIn(context, account),
              AccountMissing() => _message(context, t.noAccount, t.backToGuest),
              AccountFailed(:final message) => _message(
                context,
                message,
                t.backToGuest,
              ),
              null => _guest(context),
            },
            SizedBox(height: gap),
            _languagePreferences(context),
            const SizedBox(height: 8),
            Text(
              t.guestBrowseNote,
              style: theme.text('caption', color: theme.palette.textSecondary),
            ),
            const SizedBox(height: 520),
            _appearancePreferences(context),
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

  Widget _pageHeader(BuildContext context) {
    final theme = SrTheme.of(context);
    final locale = Localizations.localeOf(context).languageCode.toUpperCase();
    return Row(
      children: [
        Expanded(
          child: Text(
            AppLocalizations.of(context).tabAccount,
            style: theme.text('heading'),
          ),
        ),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 9),
          decoration: BoxDecoration(
            color: theme.palette.bgSurface,
            borderRadius: BorderRadius.circular(24),
            boxShadow: [
              BoxShadow(
                color: theme.palette.effectShadowKey.withValues(alpha: .12),
                blurRadius: 14,
                offset: const Offset(0, 4),
              ),
            ],
          ),
          child: Text(locale, style: theme.text('label')),
        ),
        const SizedBox(width: 8),
        Container(
          width: 40,
          height: 40,
          decoration: BoxDecoration(
            color: theme.palette.bgSurface,
            shape: BoxShape.circle,
            boxShadow: [
              BoxShadow(
                color: theme.palette.effectShadowKey.withValues(alpha: .12),
                blurRadius: 14,
                offset: const Offset(0, 4),
              ),
            ],
          ),
          child: Icon(
            Icons.notifications_none,
            size: 19,
            color: theme.palette.textPrimary,
          ),
        ),
      ],
    );
  }

  Widget _guest(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _AccountCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _Eyebrow(text: 'GUEST', theme: theme),
              const SizedBox(height: 8),
              Semantics(
                header: true,
                child: Text(t.guest, style: theme.text('title')),
              ),
              const SizedBox(height: 4),
              Text(
                t.guestNotice,
                style: theme.text('body', color: theme.palette.textSecondary),
              ),
              if (controller.signInFailed) ...[
                SizedBox(height: gap / 2),
                Text(
                  t.signInFailed,
                  key: const Key('notice'),
                  style: theme.text(
                    'body',
                    color: theme.palette.statusDangerInk,
                  ),
                ),
              ],
            ],
          ),
        ),
        const SizedBox(height: 10),
        SizedBox(
          width: double.infinity,
          child: FilledButton(
            onPressed: controller.busy ? null : controller.signIn,
            child: Text(t.signIn),
          ),
        ),
      ],
    );
  }

  Widget _signedIn(BuildContext context, Account account) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _AccountCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _Eyebrow(text: 'ACCOUNT', theme: theme),
              const SizedBox(height: 8),
              Semantics(
                header: true,
                child: Text(account.displayName, style: theme.text('title')),
              ),
              SizedBox(height: gap / 2),
              Text(
                account.roles.join(', '),
                style: theme.text('body', color: theme.palette.textSecondary),
              ),
            ],
          ),
        ),
        const SizedBox(height: 10),
        OutlinedButton(
          onPressed: controller.busy ? null : controller.signOut,
          child: Text(t.signOut),
        ),
      ],
    );
  }

  Widget _message(BuildContext context, String text, String action) {
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    return _AccountCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            text,
            style: theme.text('body'),
            key: const Key('account-message'),
          ),
          SizedBox(height: gap),
          OutlinedButton(
            onPressed: controller.busy ? null : controller.signOut,
            child: Text(action),
          ),
        ],
      ),
    );
  }

  Widget _languagePreferences(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final currentLanguage =
        preferences.locale?.languageCode ??
        Localizations.localeOf(context).languageCode;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _AccountCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Semantics(
                header: true,
                child: Text(t.language, style: theme.text('body-l')),
              ),
              const SizedBox(height: 4),
              Row(
                children: [
                  for (final code in const ['si', 'ta', 'en'])
                    Expanded(
                      child: Semantics(
                        key: Key('language-$code'),
                        button: true,
                        selected: currentLanguage == code,
                        label: _languageNames[code],
                        onTap: () => preferences.setLanguageCode(code),
                        child: ExcludeSemantics(
                          child: InkWell(
                            onTap: () => preferences.setLanguageCode(code),
                            child: ConstrainedBox(
                              constraints: const BoxConstraints(minHeight: 48),
                              child: Center(
                                child: Text(
                                  _languageNames[code]!,
                                  style: theme.text(
                                    'caption',
                                    color: theme.palette.accentPrimary,
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ],
          ),
        ),
      ],
    );
  }

  Widget _appearancePreferences(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    Widget chip({
      required String label,
      required bool selected,
      required VoidCallback onTap,
      Key? key,
    }) {
      final ink = selected
          ? theme.palette.navActiveInk
          : theme.palette.textPrimary;
      return ChoiceChip(
        key: key,
        label: Text(label, style: theme.text('body', color: ink)),
        selected: selected,
        showCheckmark: false,
        selectedColor: theme.palette.navActive,
        backgroundColor: theme.palette.bgSurface,
        side: BorderSide(
          color: selected
              ? theme.palette.navActive
              : theme.palette.textTertiary,
          width: 1.5,
        ),
        materialTapTargetSize: MaterialTapTargetSize.padded,
        padding: EdgeInsets.symmetric(horizontal: gap / 2, vertical: gap / 2),
        onSelected: (_) => onTap(),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _AccountCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Semantics(
                header: true,
                child: Text(
                  t.appearance,
                  key: const Key('appearance-title'),
                  style: theme.text('body-l'),
                ),
              ),
              SizedBox(height: gap / 2),
              Semantics(
                header: true,
                child: Text(t.theme, style: theme.text('heading')),
              ),
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
              Text(
                t.preferencesNote,
                style: theme.text(
                  'caption',
                  color: theme.palette.textSecondary,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _AccountCard extends StatelessWidget {
  const _AccountCard({required this.child});
  final Widget child;
  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    return DecoratedBox(
      decoration: BoxDecoration(
        color: theme.palette.bgSurface,
        borderRadius: BorderRadius.circular(22),
        boxShadow: [
          BoxShadow(
            color: theme.palette.effectShadowKey.withValues(alpha: .08),
            blurRadius: 16,
            offset: const Offset(0, 5),
          ),
        ],
      ),
      child: Padding(padding: const EdgeInsets.all(16), child: child),
    );
  }
}

class _Eyebrow extends StatelessWidget {
  const _Eyebrow({required this.text, required this.theme});
  final String text;
  final SrTheme theme;
  @override
  Widget build(BuildContext context) => DecoratedBox(
    decoration: BoxDecoration(
      color: theme.palette.bgMap,
      borderRadius: BorderRadius.circular(20),
    ),
    child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      child: Text(
        text,
        style: theme
            .text('caption', color: theme.palette.textSecondary)
            .copyWith(fontSize: 10, fontWeight: FontWeight.w600),
      ),
    ),
  );
}
