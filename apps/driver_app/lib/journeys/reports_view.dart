import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../driver_frame.dart';
import 'briefing_api.dart';
import 'report_api.dart';
import 'report_view.dart';

/// Tablet Reports list for one journey. Presentation only: reads, drafts and navigation stay in
/// the report screen. An unconfirmed draft is listed first, because it needs the driver's attention.
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
  });

  final DriverJourney journey;
  final List<DriverReport> reports;

  /// Reason code of this journey's unconfirmed report, if one is saved on the device.
  final String? draftReason;
  final bool busy, accessLost, canCreate, contextAvailable;
  final String? notice;
  final String displayName;
  final DateTime? now, checkedAt;
  final VoidCallback? onReport, onOpenDraft;
  final ValueChanged<DriverReport>? onOpenReport;
  final ValueChanged<String> onSelect;

  bool get _empty => reports.isEmpty && draftReason == null;

  @override
  Widget build(BuildContext context) {
    return DriverFrame(
      title: 'Reports',
      displayName: displayName,
      selected: 'reports',
      now: now,
      connection: DriverConnection(
        label: checkedAt == null
            ? 'Not checked'
            : 'Live · ${driverTime(checkedAt)}',
        showDot: checkedAt != null,
      ),
      onSelect: onSelect,
      child: SingleChildScrollView(
        key: const Key('reports-scroll'),
        padding: const EdgeInsets.fromLTRB(32, 24, 32, 10),
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
            if (_empty && !busy && notice == null && !accessLost)
              ..._emptyBody(context)
            else
              ..._listBody(context),
          ],
        ),
      ),
    );
  }

  Widget _heading(BuildContext context) {
    final date = contextAvailable
        ? reportDate(journey.serviceDate)
        : 'Service date unavailable';
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
                  '${journey.trainNumber} · $date',
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
              onPressed: accessLost || busy || !canCreate ? null : onReport,
            ),
          ),
        ],
      ),
    );
  }

  List<Widget> _listBody(BuildContext context) => [
    _heading(context),
    const SizedBox(height: 24),
    if (draftReason != null) ...[
      _ReportRow(
        id: 'draft',
        reason: reportReasons[draftReason] ?? draftReason!,
        copy: 'Saved on this device',
        tone: _Tone.danger,
        chip: 'RECEIPT NOT CONFIRMED',
        semantics:
            '${reportReasons[draftReason] ?? draftReason}. Receipt not confirmed. Saved on this device.',
        onTap: onOpenDraft,
      ),
      const SizedBox(height: 24),
    ],
    for (final report in reports) ...[_row(report), const SizedBox(height: 24)],
    if (reports.isNotEmpty || draftReason != null)
      DriverText(
        'Acknowledgement is recorded only when a controller acts.',
        key: const Key('reports-note'),
        style: driverText(context, 16, 22, secondary: true),
      ),
  ];

  Widget _row(DriverReport report) {
    final ack =
        report.state == 'ACKNOWLEDGED' &&
        report.acknowledgedByName != null &&
        report.acknowledgedAt != null;
    final reason = reportReasons[report.reasonCode] ?? report.reasonCode;
    final received = 'Server received ${driverTime(report.receivedAt)}';
    final by = ack
        ? '${report.acknowledgedByName} · ${driverTime(report.acknowledgedAt)}'
        : null;
    return _ReportRow(
      id: report.id,
      reason: reason,
      copy: received,
      tone: ack ? _Tone.success : _Tone.accent,
      chip: ack ? 'ACKNOWLEDGED' : 'AWAITING CONTROL',
      detail: by,
      semantics: ack
          ? '$reason. $received. Acknowledged by $by.'
          : '$reason. $received. Awaiting Control.',
      onTap: onOpenReport == null ? null : () => onOpenReport!(report),
    );
  }

  List<Widget> _emptyBody(BuildContext context) {
    final p = SrTheme.of(context).palette;
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
                onPressed: !canCreate ? null : onReport,
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

enum _Tone { danger, success, accent }

class _ReportRow extends StatelessWidget {
  const _ReportRow({
    required this.id,
    required this.reason,
    required this.copy,
    required this.tone,
    required this.chip,
    required this.semantics,
    this.detail,
    this.onTap,
  });
  final String id, reason, copy, chip, semantics;
  final String? detail;
  final _Tone tone;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    final (soft, ink, icon) = switch (tone) {
      _Tone.danger => (p.statusDangerSoft, p.statusDangerInk, p.statusDanger),
      _Tone.success => (
        p.statusSuccessSoft,
        p.statusSuccessInk,
        p.statusSuccess,
      ),
      _Tone.accent => (p.accentSoft, p.textPrimary, p.accentPrimary),
    };
    return Semantics(
      button: true,
      label: semantics,
      excludeSemantics: true,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 112),
        child: Material(
          key: Key('report-row-$id'),
          color: p.bgSurface,
          borderRadius: BorderRadius.circular(28),
          child: InkWell(
            borderRadius: BorderRadius.circular(28),
            onTap: onTap,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
              child: Row(
                children: [
                  Container(
                    key: Key('row-icon-$id'),
                    width: 64,
                    height: 64,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: soft,
                      borderRadius: BorderRadius.circular(28),
                    ),
                    child: DriverAsset('reports', size: 32, color: icon),
                  ),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        DriverText(
                          reason,
                          key: Key('row-title-$id'),
                          style: driverText(
                            context,
                            20,
                            28,
                            weight: FontWeight.w500,
                          ),
                        ),
                        const SizedBox(height: 4),
                        DriverText(
                          copy,
                          key: Key('row-copy-$id'),
                          style: driverText(context, 16, 22, secondary: true),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 8),
                  SizedBox(
                    key: Key('row-delivery-$id'),
                    width: 280,
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.end,
                      children: [
                        Container(
                          key: Key('row-chip-$id'),
                          padding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 8,
                          ),
                          decoration: BoxDecoration(
                            color: soft,
                            borderRadius: BorderRadius.circular(999),
                          ),
                          child: DriverText(
                            chip,
                            style: driverText(
                              context,
                              14,
                              20,
                              weight: FontWeight.w600,
                              color: ink,
                            ),
                          ),
                        ),
                        if (detail != null) ...[
                          const SizedBox(height: 8),
                          DriverText(
                            detail!,
                            key: Key('row-ack-$id'),
                            style: driverText(context, 16, 22, secondary: true),
                          ),
                        ],
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
