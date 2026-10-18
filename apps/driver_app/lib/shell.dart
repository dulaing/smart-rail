import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'account/account_controller.dart';
import 'auth/account_api.dart';
import 'l10n/app_localizations.dart';
import 'journeys/briefing_api.dart';
import 'journeys/journey_screens.dart';
import 'journeys/report_api.dart';
import 'journeys/report_screens.dart';

/// The driver destinations from the hi-fi navigation.
enum DriverTab { journeys, updates, reports, account }

/// Which destinations this account may open. Hiding a tab is a convenience only: the server
/// checks every read and action, and a denied destination still shows an access state.
bool canOpen(DriverTab tab, Account account) {
  final held = account.permissions;
  return switch (tab) {
    DriverTab.journeys ||
    DriverTab.updates => held.contains('driver.briefing.read'),
    DriverTab.reports =>
      held.contains('driver.reports.read') ||
          held.contains('driver.reports.create'),
    DriverTab.account => true,
  };
}

const _icons = {
  DriverTab.journeys: (Icons.train_outlined, Icons.train),
  DriverTab.updates: (Icons.notifications_none, Icons.notifications),
  DriverTab.reports: (Icons.edit_note_outlined, Icons.edit_note),
  DriverTab.account: (Icons.person_outline, Icons.person),
};

/// Light staff shell: navigation for the permitted destinations with empty content until the
/// journey features exist. A rail on tablets, a bottom bar on phones.
class DriverShell extends StatefulWidget {
  const DriverShell({
    super.key,
    required this.account,
    required this.controller,
    this.journeyApi,
    this.reportApi,
    this.offlineOwnerId,
  });

  final Account account;
  final AccountController controller;
  final DriverBriefingApi? journeyApi;
  final DriverReportApi? reportApi;
  final String? offlineOwnerId;

  @override
  State<DriverShell> createState() => _DriverShellState();
}

class _DriverShellState extends State<DriverShell> {
  late DriverTab _tab = _tabs.first;

  List<DriverTab> get _tabs => [
    for (final tab in DriverTab.values)
      if (canOpen(tab, widget.account)) tab,
  ];

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final tabs = _tabs;
    if (!tabs.contains(_tab)) _tab = tabs.first;

    String label(DriverTab tab) => switch (tab) {
      DriverTab.journeys => t.tabJourneys,
      DriverTab.updates => t.tabUpdates,
      DriverTab.reports => t.tabReports,
      DriverTab.account => t.tabAccount,
    };

    final nav = [for (final tab in tabs) (tab, label(tab))];
    final content = _Content(
      title: label(_tab),
      child: switch (_tab) {
        DriverTab.account => SingleChildScrollView(
          child: _AccountBody(
            account: widget.account,
            controller: widget.controller,
            hasDriverAccess: tabs.length > 1,
            offline: widget.offlineOwnerId != null,
          ),
        ),
        _ when !canOpen(_tab, widget.account) => Text(t.accessDenied),
        DriverTab.journeys || DriverTab.updates =>
          widget.journeyApi == null
              ? Text(
                  t.notAvailableYet,
                  style: theme.text('body', color: theme.palette.textSecondary),
                )
              : DriverJourneysScreen(
                  api: widget.journeyApi!,
                  offlineOwnerId: widget.offlineOwnerId,
                ),
        DriverTab.reports =>
          widget.journeyApi == null || widget.reportApi == null
              ? Text(
                  t.notAvailableYet,
                  style: theme.text('body', color: theme.palette.textSecondary),
                )
              : DriverReportsScreen(
                  journeyApi: widget.journeyApi!,
                  reportApi: widget.reportApi!,
                ),
      },
    );

    return Scaffold(
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, constraints) {
            final wide = constraints.maxWidth >= 720;
            final bar = _Nav(
              label: t.navLabel,
              items: nav,
              selected: _tab,
              vertical: wide,
              onSelect: (tab) => setState(() => _tab = tab),
            );
            return wide
                ? Row(
                    children: [
                      bar,
                      Expanded(child: content),
                    ],
                  )
                : Column(
                    children: [
                      Expanded(child: content),
                      bar,
                    ],
                  );
          },
        ),
      ),
    );
  }
}

class _Content extends StatelessWidget {
  const _Content({required this.title, required this.child});

  final String title;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final locale = Localizations.localeOf(context).languageCode;
    final gap = theme.metrics.space[24]!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: EdgeInsets.fromLTRB(gap, gap, gap, gap / 2),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                srMessages['footer.simulation']![locale] ??
                    srMessages['footer.simulation']!['en']!,
                style: theme.text('caption', color: theme.palette.textSecondary),
              ),
              SizedBox(height: gap / 2),
              Semantics(
                header: true,
                child: Text(title, style: theme.text('title')),
              ),
            ],
          ),
        ),
        // The child owns its own scrolling (a list or a column), so it must get a bounded
        // height: an unbounded one collapses a nested scrollable to nothing.
        Expanded(child: child),
      ],
    );
  }
}

class _AccountBody extends StatelessWidget {
  const _AccountBody({
    required this.account,
    required this.controller,
    required this.hasDriverAccess,
    this.offline = false,
  });

  final Account account;
  final AccountController controller;
  final bool hasDriverAccess;
  final bool offline;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(account.displayName, style: theme.text('heading')),
        SizedBox(height: gap / 2),
        Text(
          account.roles.join(', '),
          style: theme.text('body', color: theme.palette.textSecondary),
        ),
        if (!hasDriverAccess) ...[
          SizedBox(height: gap),
          Text(
            t.noDriverAccess,
            key: const Key('no-driver-access'),
            style: theme.text('body', color: theme.palette.statusDangerInk),
          ),
        ],
        SizedBox(height: gap),
        if (offline)
          OutlinedButton(
            onPressed: controller.busy ? null : controller.signIn,
            child: const Text('Reconnect and sign in'),
          )
        else
          OutlinedButton(
            onPressed: controller.busy ? null : controller.signOut,
            child: Text(t.signOut),
          ),
        if (kDebugMode) ...[
          SizedBox(height: gap / 2),
          TextButton(
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(builder: (_) => const StatusSample()),
            ),
            child: Text(t.statusSample),
          ),
        ],
      ],
    );
  }
}

class _Nav extends StatelessWidget {
  const _Nav({
    required this.label,
    required this.items,
    required this.selected,
    required this.vertical,
    required this.onSelect,
  });

  final String label;
  final List<(DriverTab, String)> items;
  final DriverTab selected;
  final bool vertical;
  final ValueChanged<DriverTab> onSelect;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final palette = theme.palette;
    final gap = theme.metrics.space[8]!;

    final children = [
      for (final (tab, text) in items)
        _NavItem(
          key: Key('tab-${tab.name}'),
          label: text,
          icons: _icons[tab]!,
          selected: tab == selected,
          onTap: () => onSelect(tab),
        ),
    ];

    // Navigation labels grow with the system text size up to 160%, so the bar cannot outgrow the screen.
    return MediaQuery.withClampedTextScaling(
      maxScaleFactor: 1.6,
      child: Semantics(
        container: true,
        explicitChildNodes: true,
        label: label,
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: palette.bgSurface,
            border: Border(
              right: vertical
                  ? BorderSide(color: palette.lineHairline)
                  : BorderSide.none,
              top: vertical
                  ? BorderSide.none
                  : BorderSide(color: palette.lineHairline),
            ),
          ),
          child: Padding(
            padding: EdgeInsets.all(gap),
            child: vertical
                ? SizedBox(
                    width: 128,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        for (final c in children)
                          Padding(
                            padding: EdgeInsets.only(bottom: gap),
                            child: c,
                          ),
                      ],
                    ),
                  )
                : Row(children: [for (final c in children) Expanded(child: c)]),
          ),
        ),
      ),
    );
  }
}

class _NavItem extends StatelessWidget {
  const _NavItem({
    super.key,
    required this.label,
    required this.icons,
    required this.selected,
    required this.onTap,
  });

  final String label;
  final (IconData, IconData) icons;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final palette = theme.palette;
    final ink = selected ? palette.navActiveInk : palette.textPrimary;
    final radius = BorderRadius.circular(theme.metrics.radius['md']!);

    return Semantics(
      button: true,
      selected: selected,
      inMutuallyExclusiveGroup: true,
      label: label,
      onTap: onTap,
      child: ExcludeSemantics(
        child: Material(
          color: selected ? palette.navActive : Colors.transparent,
          borderRadius: radius,
          child: InkWell(
            borderRadius: radius,
            onTap: onTap,
            child: ConstrainedBox(
              constraints: BoxConstraints(
                minHeight: theme.metrics.minTarget + 16,
              ),
              child: Padding(
                padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    Icon(selected ? icons.$2 : icons.$1, color: ink, size: 28),
                    const SizedBox(height: 2),
                    FittedBox(
                      fit: BoxFit.scaleDown,
                      child: Text(
                        label,
                        style: theme.text('nav', color: ink),
                        maxLines: 1,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
