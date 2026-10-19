import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../driver_frame.dart';
import 'driver_rows.dart';
import 'report_view.dart';

/// One selectable row of the selector: a journey, or an unconfirmed report.
class SelectorItem {
  const SelectorItem({
    required this.id,
    required this.rowKey,
    required this.title,
    required this.copy,
    required this.semantics,
    this.chip,
    this.detail,
    this.tone = RowTone.neutral,
    this.icon = 'briefing',
    this.onTap,
  });
  final String id, title, copy, semantics;
  final Key rowKey;
  final String? chip, detail;
  final RowTone tone;
  final String icon;
  final VoidCallback? onTap;
}

/// What an empty list means. Offline the app cannot know whether anything is assigned, so only an
/// online read may say "No duty assignment".
enum SelectorEmpty { none, noAssignment, noSaved }

/// Journey selector for Briefing, Updates and the Reports chooser. Presentation only: reads, the
/// saved copy and navigation stay in the owning screen.
class DriverSelectorView extends StatelessWidget {
  const DriverSelectorView({
    super.key,
    required this.destination,
    required this.displayName,
    required this.date,
    required this.items,
    required this.onPreviousDate,
    required this.onNextDate,
    required this.onSelect,
    this.pending = const [],
    this.empty = SelectorEmpty.none,
    this.loading = false,
    this.error,
    this.onRetry,
    this.unavailable = false,
    this.saved = false,
    this.now,
    this.checkedAt,
    this.showJourneys = true,
    this.showReports = true,
    this.onCheckAgain,
    this.onRefresh,
  });

  /// `journeys`, `updates` or `reports`.
  final String destination, displayName;
  final DateTime date;
  final List<SelectorItem> items, pending;
  final VoidCallback onPreviousDate, onNextDate;
  final ValueChanged<String> onSelect;
  final SelectorEmpty empty;
  final bool loading, unavailable, saved, showJourneys, showReports;
  final String? error;
  final VoidCallback? onRetry, onCheckAgain;
  final DateTime? now, checkedAt;
  final Future<void> Function()? onRefresh;

  bool get _reports => destination == 'reports';

  String get _lead => switch (destination) {
    'updates' => 'Choose a journey to see its confirmed updates.',
    'reports' => 'Choose a journey for your report.',
    _ => 'Choose a journey to open its briefing.',
  };

  @override
  Widget build(BuildContext context) {
    final wide =
        MediaQuery.sizeOf(context).width >= 1000 &&
        MediaQuery.textScalerOf(context).scale(20) <= 28;
    final day = driverDay(date);
    final title = wide
        ? switch (destination) {
            'updates' => 'Updates · $day',
            'reports' => 'Reports · $day',
            _ => 'Today’s duty · $day',
          }
        : switch (destination) {
            'updates' => 'Updates',
            'reports' => 'Reports',
            _ => 'Briefing',
          };
    final gap = wide ? 24.0 : 32 / 3;
    final emptyState = empty != SelectorEmpty.none;
    final retry = error != null && onRetry != null;
    final body = <Widget>[
      if (!wide) _PhoneChips(saved: saved),
      if (!wide) SizedBox(height: gap),
      if (wide)
        _heading(context, emptyState ? 'Service date' : _lead)
      else ...[
        if (!emptyState) ...[
          DriverText(
            _lead,
            key: const Key('selector-lead'),
            style: driverText(context, 16, 22, secondary: true),
          ),
          SizedBox(height: gap),
        ],
        DriverDateControl(
          label: day,
          onPrevious: onPreviousDate,
          onNext: onNextDate,
          wide: false,
        ),
      ],
      SizedBox(height: gap),
      if (loading) ...[const LinearProgressIndicator(), SizedBox(height: gap)],
      if (error != null) ...[
        _Notice(
          key: const Key('journey-list-error'),
          message: error!,
          wide: wide,
          onRetry: retry ? onRetry : null,
        ),
        SizedBox(height: gap),
      ],
      if (empty == SelectorEmpty.noAssignment) ..._noAssignment(context, wide),
      if (empty == SelectorEmpty.noSaved) ..._noSaved(context, wide),
      if (!emptyState) ..._rows(context, wide, gap),
      if (unavailable && !emptyState && error == null && !loading)
        _Notice(message: 'The journey list is unavailable.', wide: wide),
    ];
    final scroll = SingleChildScrollView(
      key: const Key('driver-journeys-list'),
      physics: const AlwaysScrollableScrollPhysics(),
      padding: EdgeInsets.fromLTRB(
        wide ? 32 : 55 / 3,
        wide ? 24 : 16,
        wide ? 32 : 55 / 3,
        wide ? 10 : 16,
      ),
      child: Column(
        key: const Key('selector-content'),
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: body,
      ),
    );
    final label = saved
        ? 'Offline'
        : checkedAt == null
        ? 'Not checked'
        : 'Checked ${driverTime(checkedAt)}';
    return DriverFrame(
      title: title,
      displayName: displayName,
      selected: destination,
      now: now,
      showJourneys: showJourneys,
      showReports: showReports,
      connection: wide
          ? DriverConnection(
              label: label,
              showDot: saved || checkedAt != null,
              warning: saved,
            )
          : null,
      onSelect: onSelect,
      phoneAction: !wide && emptyState && onCheckAgain != null
          ? DriverAction(
              key: const Key('state-action'),
              label: empty == SelectorEmpty.noSaved
                  ? 'Retry connection'
                  : 'Check again',
              phone: true,
              onPressed: onCheckAgain,
            )
          : null,
      child: onRefresh == null
          ? scroll
          : RefreshIndicator(onRefresh: onRefresh!, child: scroll),
    );
  }

  Widget _heading(BuildContext context, String lead) => SizedBox(
    key: const Key('selector-heading'),
    height: 56,
    child: Row(
      children: [
        Expanded(
          child: DriverText(
            lead,
            key: const Key('selector-lead'),
            style: driverText(context, 20, 28, secondary: true),
          ),
        ),
        const SizedBox(width: 24),
        DriverDateControl(
          label: driverDay(date),
          onPrevious: onPreviousDate,
          onNext: onNextDate,
        ),
      ],
    ),
  );

  List<Widget> _rows(BuildContext context, bool wide, double gap) {
    Widget row(SelectorItem item) => DriverRow(
      id: item.id,
      rowKey: item.rowKey,
      wide: wide,
      title: item.title,
      copy: item.copy,
      chip: item.chip,
      detail: item.detail,
      tone: item.tone,
      icon: item.icon,
      semantics: item.semantics,
      onTap: item.onTap,
    );
    return [
      if (pending.isNotEmpty) ...[
        DriverSection(
          key: const Key('section-unconfirmed'),
          title: 'Reports not confirmed',
          note: 'Control may not have received these. Open one to check receipt or retry the same report. Nothing is sent automatically.',
        ),
        SizedBox(height: gap),
        for (final item in pending) ...[row(item), SizedBox(height: gap)],
      ],
      if (_reports && pending.isNotEmpty && items.isNotEmpty) ...[
        DriverSection(
          key: const Key('section-journeys'),
          title: 'Today’s journeys',
        ),
        SizedBox(height: gap),
      ],
      for (final item in items) ...[row(item), SizedBox(height: gap)],
      if (_reports && (items.isNotEmpty || pending.isNotEmpty))
        DriverText(
          'Urgent? Use the radio.',
          key: const Key('reports-note'),
          style: driverText(context, 16, 22, secondary: true),
        ),
    ];
  }

  List<Widget> _noAssignment(BuildContext context, bool wide) {
    final gap = wide ? 24.0 : 32 / 3;
    final checked = checkedAt == null
        ? null
        : 'Last checked ${reportTime(checkedAt!)}';
    return [
      DriverStatePanel(
        key: const Key('no-assigned-journeys'),
        panelKey: 'state-panel',
        prefix: 'state',
        wide: wide,
        icon: wide ? 'briefing' : 'phone-state-briefing',
        title: 'No duty assignment',
        copy: wide
            ? 'You’re signed in as $displayName. Check again or contact Control.'
                  '${checked == null ? '' : '\n$checked · ${driverLongDay(date)}.'}'
            : 'No train is assigned to your staff account.',
        detail: wide ? null : checked,
        actionLabel: wide ? 'Check again' : null,
        onAction: onCheckAgain,
      ),
      SizedBox(height: gap),
      DriverContactCard(
        cardKey: 'state-contact',
        wide: wide,
        title: 'Contact Control',
        copy: wide
            ? 'Use your usual staff radio or phone contact. There is no in-app chat.'
            : 'Use your usual staff radio or phone to confirm your assignment.',
      ),
    ];
  }

  List<Widget> _noSaved(BuildContext context, bool wide) {
    final gap = wide ? 24.0 : 32 / 3;
    return [
      DriverStatePanel(
        key: const Key('no-saved-journeys'),
        panelKey: 'state-panel',
        prefix: 'state',
        wide: wide,
        icon: 'phone-radio',
        title: 'No saved briefing available',
        copy: wide
            ? 'You’re offline and this tablet has no saved copy.\nConnect to download today’s assignment and confirmed changes.'
            : 'You’re offline and this phone has no saved copy. Connect to download today’s assignment and confirmed changes.',
        actionLabel: wide ? 'Retry connection' : null,
        onAction: onCheckAgain,
      ),
      SizedBox(height: gap),
      ReportGuidance(
        'Urgent? Use the radio. Live train information is unavailable.',
        wide: wide,
      ),
    ];
  }
}

class _PhoneChips extends StatelessWidget {
  const _PhoneChips({required this.saved});
  final bool saved;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    Widget chip(Key? key, String text) => Container(
      key: key,
      padding: const EdgeInsets.all(16 / 3),
      decoration: BoxDecoration(
        color: p.bgSurface2,
        borderRadius: BorderRadius.circular(999),
      ),
      child: DriverText(
        text,
        style: driverText(
          context,
          14,
          20,
          weight: FontWeight.w600,
          secondary: true,
        ),
      ),
    );
    return Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        if (saved) chip(null, 'SAVED COPY'),
        chip(const Key('selector-chip'), 'SIMULATED DATA'),
      ],
    );
  }
}

class _Notice extends StatelessWidget {
  const _Notice({
    super.key,
    required this.message,
    required this.wide,
    this.onRetry,
  });
  final String message;
  final bool wide;
  final VoidCallback? onRetry;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return Semantics(
      liveRegion: true,
      child: Container(
        padding: EdgeInsets.all(wide ? 24 : 40 / 3),
        decoration: BoxDecoration(
          color: p.bgSurface,
          borderRadius: BorderRadius.circular(wide ? 28 : 44 / 3),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            DriverText(message, style: driverText(context, 20, 28)),
            if (onRetry != null) ...[
              const SizedBox(height: 8),
              Align(
                alignment: Alignment.centerLeft,
                child: SizedBox(
                  width: wide ? 480 : null,
                  child: DriverAction(
                    label: 'Try again',
                    outlined: true,
                    phone: !wide,
                    onPressed: onRetry,
                  ),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// `Monday 12 October`, as in the no-duty reference.
String driverLongDay(DateTime date) {
  const days = [
    'Monday',
    'Tuesday',
    'Wednesday',
    'Thursday',
    'Friday',
    'Saturday',
    'Sunday',
  ];
  const months = [
    'January',
    'February',
    'March',
    'April',
    'May',
    'June',
    'July',
    'August',
    'September',
    'October',
    'November',
    'December',
  ];
  return '${days[date.weekday - 1]} ${date.day} ${months[date.month - 1]}';
}
