import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../driver_frame.dart';
import 'briefing_api.dart';
import 'driver_rows.dart';
import 'report_api.dart';
import 'report_view.dart';

/// Reports list for one journey, on tablet and phone. Presentation only: reads, drafts and
/// navigation stay in the report screen. An unconfirmed draft is listed first, because it needs
/// the driver's attention.
class DriverReportsListView extends StatelessWidget {
  const DriverReportsListView({
    super.key,
    required this.journey,
    required this.reports,
    required this.onSelect,
    this.draftReason,
    this.busy = false,
    this.accessLost = false,
    this.canCreate = true,
    this.contextAvailable = true,
    this.notice,
    this.displayName = 'Driver',
    this.now,
    this.checkedAt,
    this.onReport,
    this.onOpenReport,
    this.onOpenDraft,
    this.onBack,
  });

  final DriverJourney journey;
  final List<DriverReport> reports;

  /// Reason code of this journey's unconfirmed report, if one is saved on the device.
  final String? draftReason;
  final bool busy, accessLost, canCreate, contextAvailable;
  final String? notice;
  final String displayName;
  final DateTime? now, checkedAt;
  final VoidCallback? onReport, onOpenDraft, onBack;
  final ValueChanged<DriverReport>? onOpenReport;
  final ValueChanged<String> onSelect;

  bool get _empty => reports.isEmpty && draftReason == null;

  @override
  Widget build(BuildContext context) {
    final wide =
        MediaQuery.sizeOf(context).width >= 1000 &&
        MediaQuery.textScalerOf(context).scale(20) <= 28;
    final unavailable = accessLost || busy || !canCreate || onReport == null;
    final empty = _empty && !busy && notice == null && !accessLost;
    return DriverFrame(
      title: wide ? 'Reports' : 'Your reports',
      displayName: displayName,
      selected: 'reports',
      now: now,
      connection: wide
          ? DriverConnection(
              label: checkedAt == null
                  ? 'Not checked'
                  : 'Live · ${driverTime(checkedAt)}',
              showDot: checkedAt != null,
            )
          : null,
      onPhoneBack: wide ? null : onBack,
      onSelect: onSelect,
      phoneAction: wide
          ? null
          : DriverAction(
              key: const Key('reports-primary'),
              label: 'Report a problem',
              phone: true,
              onPressed: unavailable ? null : onReport,
            ),
      child: SingleChildScrollView(
        key: const Key('reports-scroll'),
        padding: EdgeInsets.fromLTRB(
          wide ? 32 : 55 / 3,
          wide ? 24 : 16,
          wide ? 32 : 55 / 3,
          wide ? 10 : 16,
        ),
        child: Column(
          key: const Key('reports-content'),
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (busy) const LinearProgressIndicator(),
            if (notice != null || accessLost)
              Padding(
                padding: const EdgeInsets.only(bottom: 16),
                child: Semantics(
                  liveRegion: true,
                  child: DriverText(
                    accessLost
                        ? 'Access ended. Sign in again or contact Control.'
                        : notice!,
                    key: const Key('reports-notice'),
                    style: driverText(context, 20, 28),
                  ),
                ),
              ),
            // The approved empty list has no heading: its panel starts the page.
            if (!(wide && empty)) ...[
              if (wide) _heading(context, unavailable) else _context(context),
              SizedBox(height: wide ? 24 : 32 / 3),
            ],
            if (empty)
              ..._emptyBody(context, wide, unavailable)
            else
              ..._listBody(context, wide),
          ],
        ),
      ),
    );
  }

  String get _date => contextAvailable
      ? reportDate(journey.serviceDate)
      : 'Service date unavailable';

  Widget _context(BuildContext context) => DriverText(
    '${journey.trainNumber} · $_date',
    key: const Key('reports-context'),
    style: driverText(context, 20, 28, secondary: true),
  );

  Widget _heading(BuildContext context, bool unavailable) {
    return ConstrainedBox(
      key: const Key('reports-heading'),
      constraints: const BoxConstraints(minHeight: 96),
      child: Row(
        children: [
          SizedBox(
            key: const Key('reports-context'),
            width: 560,
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Semantics(
                  header: true,
                  child: DriverText(
                    'Your reports',
                    style: driverText(context, 28, 36, weight: FontWeight.w600),
                  ),
                ),
                const SizedBox(height: 8),
                DriverText(
                  '${journey.trainNumber} · $_date',
                  style: driverText(context, 20, 28, secondary: true),
                ),
              ],
            ),
          ),
          const Spacer(),
          SizedBox(
            width: 480,
            child: DriverAction(
              key: const Key('reports-primary'),
              label: 'Report a problem',
              onPressed: unavailable ? null : onReport,
            ),
          ),
        ],
      ),
    );
  }

  List<Widget> _listBody(BuildContext context, bool wide) {
    final gap = wide ? 24.0 : 32 / 3;
    return [
      if (draftReason != null) ...[
        DriverRow(
          id: 'draft',
          rowKey: const Key('report-row-draft'),
          wide: wide,
          title: reportReasons[draftReason] ?? draftReason!,
          copy: 'Saved on this device',
          tone: RowTone.danger,
          chip: 'RECEIPT NOT CONFIRMED',
          semantics:
              '${reportReasons[draftReason] ?? draftReason}. Receipt not confirmed. Saved on this device.',
          onTap: onOpenDraft,
        ),
        SizedBox(height: gap),
      ],
      for (final report in reports) ...[
        _row(report, wide),
        SizedBox(height: gap),
      ],
      if (reports.isNotEmpty || draftReason != null)
        DriverText(
          'Acknowledgement is recorded only when a controller acts.',
          key: const Key('reports-note'),
          style: driverText(context, 16, 22, secondary: true),
        ),
    ];
  }

  Widget _row(DriverReport report, bool wide) {
    final ack =
        report.state == 'ACKNOWLEDGED' &&
        report.acknowledgedByName != null &&
        report.acknowledgedAt != null;
    final reason = reportReasons[report.reasonCode] ?? report.reasonCode;
    final received = 'Server received ${driverTime(report.receivedAt)}';
    final by = ack
        ? '${report.acknowledgedByName} · ${driverTime(report.acknowledgedAt)}'
        : null;
    return DriverRow(
      id: report.id,
      rowKey: Key('report-row-${report.id}'),
      wide: wide,
      title: reason,
      copy: received,
      tone: ack ? RowTone.success : RowTone.accent,
      chip: ack ? 'ACKNOWLEDGED' : 'AWAITING CONTROL',
      detail: by,
      semantics: ack
          ? '$reason. $received. Acknowledged by $by.'
          : '$reason. $received. Awaiting Control.',
      onTap: onOpenReport == null ? null : () => onOpenReport!(report),
    );
  }

  List<Widget> _emptyBody(BuildContext context, bool wide, bool unavailable) {
    final p = SrTheme.of(context).palette;
    if (!wide) {
      return [
        Container(
          key: const Key('reports-empty'),
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            color: p.bgSurface,
            borderRadius: BorderRadius.circular(44 / 3),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Align(
                alignment: Alignment.centerLeft,
                child: DriverAsset(
                  'reports',
                  size: 48,
                  color: p.accentPrimary,
                  key: const Key('reports-empty-icon'),
                ),
              ),
              const SizedBox(height: 40 / 3),
              Semantics(
                header: true,
                child: DriverText(
                  'No reports sent yet',
                  key: const Key('reports-empty-title'),
                  style: driverText(context, 28, 36, weight: FontWeight.w600),
                ),
              ),
              const SizedBox(height: 40 / 3),
              DriverText(
                'Reports for your assigned train will appear here. Use the report form only while stopped.',
                key: const Key('reports-empty-copy'),
                style: driverText(context, 20, 28, secondary: true),
              ),
            ],
          ),
        ),
        const SizedBox(height: 32 / 3),
        const ReportGuidance('Urgent? Use the radio.', wide: false),
      ];
    }
    return [
      Container(
        key: const Key('reports-empty'),
        constraints: const BoxConstraints(minHeight: 448),
        decoration: BoxDecoration(
          color: p.bgSurface,
          borderRadius: BorderRadius.circular(28),
        ),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Container(
              key: const Key('reports-empty-icon'),
              width: 80,
              height: 80,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: p.accentSoft,
                borderRadius: BorderRadius.circular(28),
              ),
              child: DriverAsset('reports', size: 32, color: p.accentPrimary),
            ),
            const SizedBox(height: 16),
            SizedBox(
              key: const Key('reports-empty-title'),
              width: 900,
              child: Semantics(
                header: true,
                child: DriverText(
                  'No reports sent yet',
                  textAlign: TextAlign.center,
                  style: driverText(context, 28, 36, weight: FontWeight.w600),
                ),
              ),
            ),
            const SizedBox(height: 16),
            SizedBox(
              key: const Key('reports-empty-copy'),
              width: 900,
              child: DriverText(
                'Reports for your assigned train will appear here.\n'
                'Use the report form only while stopped.',
                textAlign: TextAlign.center,
                style: driverText(context, 20, 28, secondary: true),
              ),
            ),
            const SizedBox(height: 16),
            SizedBox(
              width: 480,
              child: DriverAction(
                key: const Key('reports-empty-action'),
                label: 'Report a problem',
                onPressed: unavailable ? null : onReport,
              ),
            ),
          ],
        ),
      ),
      const SizedBox(height: 24),
      const ReportGuidance('Urgent? Use the radio.', wide: true),
    ];
  }
}
