import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart' show StatusSample;

import 'account/account_controller.dart';
import 'account/account_view.dart';
import 'auth/account_api.dart';
import 'driver_frame.dart';
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

  void _selectTab(DriverTab tab) {
    Navigator.of(context).popUntil((route) => route.isFirst);
    setState(() => _tab = tab);
  }

  /// The measured frame names its destinations; the shell owns which one is open.
  void _selectFromFrame(String destination) => _selectTab(switch (destination) {
    'journeys' => DriverTab.journeys,
    'updates' => DriverTab.updates,
    'reports' => DriverTab.reports,
    _ => DriverTab.account,
  });

  void _openReport(DriverJourney journey) {
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => DriverReportJourneyScreen(
          journey: journey,
          api: widget.reportApi!,
          journeyApi: widget.journeyApi!,
          displayName: widget.account.displayName,
          canCreate: widget.account.permissions.contains(
            'driver.reports.create',
          ),
          onAccount: () => _selectTab(DriverTab.account),
          onUpdates: () => _openBriefing(journey, updates: true),
        ),
      ),
    );
  }

  void _openBriefing(DriverJourney journey, {bool updates = false}) {
    Navigator.of(context).pushReplacement(
      MaterialPageRoute<void>(
        builder: (_) => DriverBriefingScreen(
          api: widget.journeyApi!,
          journey: journey,
          displayName: widget.account.displayName,
          updatesOnly: updates,
          onAccount: () => _selectTab(DriverTab.account),
          onReports: canOpen(DriverTab.reports, widget.account)
              ? () => _selectTab(DriverTab.reports)
              : null,
          onReport:
              widget.reportApi != null &&
                  widget.account.permissions.contains('driver.reports.create')
              ? () => _openReport(journey)
              : null,
        ),
      ),
    );
  }

  List<DriverTab> get _tabs => [
    for (final tab in DriverTab.values)
      if (canOpen(tab, widget.account)) tab,
  ];

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final tabs = _tabs;
    if (!tabs.contains(_tab)) _tab = tabs.first;

    if (_tab == DriverTab.account) {
      return DriverAccountView(
        displayName: widget.account.displayName,
        roles: widget.account.roles,
        noAccessMessage: tabs.length > 1 ? null : t.noDriverAccess,
        offline: widget.offlineOwnerId != null,
        busy: widget.controller.busy,
        now: DateTime.now(),
        checkedAt: widget.controller.accountCheckedAt,
        showJourneys: tabs.contains(DriverTab.journeys),
        showReports: tabs.contains(DriverTab.reports),
        onSignOut: widget.offlineOwnerId != null
            ? widget.controller.signIn
            : widget.controller.signOut,
        onSelect: _selectFromFrame,
        statusSample: kDebugMode
            ? () => Navigator.of(context).push(
                MaterialPageRoute<void>(builder: (_) => const StatusSample()),
              )
            : null,
      );
    }
    final reports = tabs.contains(DriverTab.reports);
    final reportsOpen = reports && widget.offlineOwnerId == null;
    return switch (_tab) {
      DriverTab.journeys || DriverTab.updates =>
        widget.journeyApi == null
            ? _Unavailable(
                title: _tab == DriverTab.updates ? 'Updates' : 'Briefing',
                selected: _tab == DriverTab.updates ? 'updates' : 'journeys',
                account: widget.account,
                showReports: reports,
                message: t.notAvailableYet,
                onSelect: _selectFromFrame,
              )
            : DriverJourneysScreen(
                api: widget.journeyApi!,
                offlineOwnerId: widget.offlineOwnerId,
                displayName: widget.account.displayName,
                updatesOnly: _tab == DriverTab.updates,
                showReports: reportsOpen,
                onSelect: _selectFromFrame,
                onReconnect: widget.controller.retry,
                onReports: !reportsOpen
                    ? null
                    : () => _selectTab(DriverTab.reports),
                onAccount: () => _selectTab(DriverTab.account),
                onReport:
                    widget.reportApi == null ||
                        !widget.account.permissions.contains(
                          'driver.reports.create',
                        )
                    ? null
                    : _openReport,
              ),
      DriverTab.reports =>
        widget.journeyApi == null || widget.reportApi == null
            ? _Unavailable(
                title: 'Reports',
                selected: 'reports',
                account: widget.account,
                showReports: reports,
                message: t.notAvailableYet,
                onSelect: _selectFromFrame,
              )
            : DriverReportsScreen(
                journeyApi: widget.journeyApi!,
                reportApi: widget.reportApi!,
                displayName: widget.account.displayName,
                showJourneys: tabs.contains(DriverTab.journeys),
                onAccount: () => _selectTab(DriverTab.account),
                onSelect: _selectFromFrame,
                canCreate: widget.account.permissions.contains(
                  'driver.reports.create',
                ),
                onBriefing: _openBriefing,
                onUpdates: (journey) => _openBriefing(journey, updates: true),
              ),
      DriverTab.account => const SizedBox.shrink(),
    };
  }
}

/// A destination whose feature is not wired into this build.
class _Unavailable extends StatelessWidget {
  const _Unavailable({
    required this.title,
    required this.selected,
    required this.account,
    required this.showReports,
    required this.message,
    required this.onSelect,
  });
  final String title, selected, message;
  final Account account;
  final bool showReports;
  final ValueChanged<String> onSelect;

  @override
  Widget build(BuildContext context) => DriverFrame(
    title: title,
    displayName: account.displayName,
    selected: selected,
    showReports: showReports,
    onSelect: onSelect,
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: DriverText(
        message,
        style: driverText(context, 20, 28, secondary: true),
      ),
    ),
  );
}
