import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../driver_frame.dart';
import 'briefing_api.dart';
import 'report_api.dart';

const reportReasons = {
  'TRAIN_FAULT': 'Train fault',
  'SIGNAL_DELAY': 'Signal delay',
  'OBSTRUCTION': 'Obstruction',
  'CANNOT_CONTINUE': 'Cannot continue',
};

/// Report presentation only. Persistence, idempotency and receipt reads stay in the controller.
class DriverReportView extends StatelessWidget {
  const DriverReportView({
    super.key,
    required this.journey,
    required this.reason,
    required this.onBack,
    this.report,
    this.hasDraft = false,
    this.contextAvailable = true,
    this.busy = false,
    this.notice,
    this.displayName = 'Driver',
    this.now,
    this.checkedAt,
    this.onReason,
    this.onSend,
    this.onCheck,
    this.onReports,
    this.onAccount,
    this.onBriefing,
    this.onUpdates,
  });

  final DriverJourney journey;
  final String reason, displayName;
  final DriverReport? report;
  final bool hasDraft, contextAvailable, busy;
  final String? notice;
  final DateTime? now, checkedAt;
  final ValueChanged<String>? onReason;
  final VoidCallback onBack;
  final VoidCallback? onSend, onCheck, onReports, onAccount;
  final VoidCallback? onBriefing, onUpdates;

  VoidCallback get _briefing => onBriefing ?? onBack;

  bool get _ack =>
      report?.state == 'ACKNOWLEDGED' &&
      report?.acknowledgedByName != null &&
      report?.acknowledgedAt != null;
  bool get _receipt => hasDraft || report != null;
  String get _reason => reportReasons[report?.reasonCode ?? reason] ?? reason;
  String get _date => contextAvailable
      ? reportDate(journey.serviceDate)
      : 'Service date unavailable';
  String get _route => contextAvailable
      ? '${journey.origin} → ${journey.destination}'
      : 'Route unavailable';

  @override
  Widget build(BuildContext context) {
    final wide =
        MediaQuery.sizeOf(context).width >= 1000 &&
        MediaQuery.textScalerOf(context).scale(20) <= 28;
    final title = wide
        ? !_receipt
              ? 'Report a problem'
              : hasDraft
              ? 'Your report · receipt not confirmed'
              : 'Your report · ${report!.id}'
        : _receipt
        ? 'Your report'
        : 'Report';
    return DriverFrame(
      title: title,
      displayName: displayName,
      selected: 'reports',
      now: now,
      connection: wide
          ? _ReportConnection(uncertain: hasDraft, checkedAt: checkedAt)
          : null,
      onPhoneBack: onBack,
      onSelect: (value) {
        if (value == 'account') onAccount?.call();
        if (value == 'reports') onReports?.call();
        if (value == 'journeys') _briefing();
        if (value == 'updates') (onUpdates ?? _briefing)();
      },
      phoneAction: wide
          ? null
          : DriverAction(
              key: const Key('report-primary'),
              label: !_receipt
                  ? 'Send report'
                  : hasDraft
                  ? 'Retry same report'
                  : _ack
                  ? 'Back to briefing'
                  : 'Check status',
              outlined: _ack,
              phone: true,
              onPressed: busy
                  ? null
                  : !_receipt || hasDraft
                  ? onSend
                  : _ack
                  ? _briefing
                  : onCheck,
            ),
      child: SingleChildScrollView(
        key: const Key('report-scroll'),
        padding: EdgeInsets.fromLTRB(
          wide ? 32 : 55 / 3,
          wide ? 24 : 16,
          wide ? 32 : 55 / 3,
          wide ? 10 : 16,
        ),
        child: Column(
          key: const Key('report-content'),
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (busy) const LinearProgressIndicator(),
            if (notice != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 16),
                child: Semantics(
                  liveRegion: true,
                  child: DriverText(
                    notice!,
                    key: const Key('report-draft-state'),
                    style: driverText(context, 20, 28),
                  ),
                ),
              ),
            if (_receipt)
              ..._receiptBody(context, wide)
            else
              ..._composer(context, wide),
          ],
        ),
      ),
    );
  }

  List<Widget> _composer(BuildContext context, bool wide) {
    final gap = wide ? 24.0 : 32 / 3;
    return [
      _ReportGuidance('Stop first. Urgent? Use the radio.', wide: wide),
      SizedBox(height: gap),
      _ReportSurface(
        surfaceKey: const Key('report-context'),
        wide: wide,
        padding: wide ? 16 : 40 / 3,
        child: wide
            ? LayoutBuilder(
                builder: (context, box) => Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    for (final pair in [
                      ('ASSIGNED TRAIN', journey.trainNumber),
                      ('SERVICE DATE', _date),
                      ('ROUTE', _route),
                    ]) ...[
                      SizedBox(
                        width: box.maxWidth * 666 / 2096,
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            DriverText(
                              pair.$1,
                              style: driverText(
                                context,
                                14,
                                20,
                                weight: FontWeight.w600,
                                secondary: true,
                              ),
                            ),
                            const SizedBox(height: 4),
                            DriverText(
                              pair.$2,
                              style: driverText(
                                context,
                                20,
                                28,
                                weight: FontWeight.w500,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(width: 8),
                    ],
                  ],
                ),
              )
            : Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  DriverText(
                    '${journey.trainNumber} · $_date',
                    style: driverText(context, 20, 28, weight: FontWeight.w500),
                  ),
                  const SizedBox(height: 8),
                  DriverText(
                    _route,
                    style: driverText(context, 16, 22, secondary: true),
                  ),
                ],
              ),
      ),
      SizedBox(height: gap),
      if (!wide) ...[
        DriverText(
          'What is the problem?',
          style: driverText(context, 22, 28, weight: FontWeight.w600),
        ),
        SizedBox(height: gap),
      ],
      _reasonChoices(context, wide),
      SizedBox(height: gap),
      if (wide)
        Row(
          children: [
            Expanded(
              flex: 1504,
              child: DriverAction(
                key: const Key('report-primary'),
                label: 'Send report',
                onPressed: busy ? null : onSend,
              ),
            ),
            const SizedBox(width: 24),
            Expanded(
              flex: 608,
              child: DriverAction(
                label: 'Back to briefing',
                outlined: true,
                onPressed: _briefing,
              ),
            ),
          ],
        )
      else
        DriverText(
          'Selected: $_reason',
          style: driverText(context, 16, 22, secondary: true),
        ),
    ];
  }

  Widget _reasonChoices(BuildContext context, bool wide) {
    final choices = [
      for (final entry in reportReasons.entries)
        _ReportReason(
          code: entry.key,
          label: entry.value,
          selected: reason == entry.key,
          wide: wide,
          onTap: busy || hasDraft || onReason == null
              ? null
              : () => onReason!(entry.key),
        ),
    ];
    return Column(
      key: const Key('report-reasons'),
      children: wide
          ? [
              for (var i = 0; i < choices.length; i += 2) ...[
                if (i > 0) const SizedBox(height: 16),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(child: choices[i]),
                    const SizedBox(width: 24),
                    Expanded(child: choices[i + 1]),
                  ],
                ),
              ],
            ]
          : [
              for (var i = 0; i < choices.length; i++) ...[
                if (i > 0) const SizedBox(height: 8),
                choices[i],
              ],
            ],
    );
  }

  List<Widget> _receiptBody(BuildContext context, bool wide) {
    final p = SrTheme.of(context).palette;
    final gap = wide ? 24.0 : 32 / 3;
    final receipt = [
      Align(
        alignment: Alignment.centerLeft,
        child: Container(
          key: const Key('receipt-badge'),
          padding: EdgeInsets.symmetric(
            horizontal: wide ? 12 : 16 / 3,
            vertical: wide ? 8 : 16 / 3,
          ),
          decoration: BoxDecoration(
            color: hasDraft
                ? p.statusDangerSoft
                : _ack
                ? p.statusSuccessSoft
                : p.accentSoft,
            borderRadius: BorderRadius.circular(999),
          ),
          child: DriverText(
            hasDraft
                ? 'RECEIPT NOT CONFIRMED'
                : _ack
                ? 'ACKNOWLEDGED'
                : 'SENT · AWAITING CONTROL',
            style: driverText(
              context,
              14,
              20,
              weight: FontWeight.w600,
              color: hasDraft
                  ? p.statusDangerInk
                  : _ack
                  ? p.statusSuccessInk
                  : p.textPrimary,
            ),
          ),
        ),
      ),
      SizedBox(height: gap),
      DriverText(
        hasDraft
            ? 'Receipt not confirmed'
            : _ack
            ? 'Acknowledged by ${report!.acknowledgedByName}'
            : wide
            ? 'Sent — awaiting Control acknowledgement'
            : 'Waiting for Control',
        key: const Key('receipt-title'),
        style: driverText(context, 28, 36, weight: FontWeight.w600),
      ),
      SizedBox(height: gap),
      DriverText(
        hasDraft
            ? 'We couldn’t confirm whether the server received this report.'
            : _ack
            ? 'Control acknowledgement · ${reportTime(report!.acknowledgedAt!)}'
            : 'Server received this report at ${reportTime(report!.receivedAt)}.',
        style: driverText(context, 20, 28, secondary: true),
      ),
      SizedBox(height: gap),
      _timeline(context, wide),
    ];
    final contextCard = _ReportSurface(
      surfaceKey: const Key('report-context'),
      wide: wide,
      padding: wide ? 20 : 40 / 3,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          DriverText(
            wide ? _reason : '$_reason · ${journey.trainNumber}',
            style: driverText(context, 20, 28, weight: FontWeight.w500),
          ),
          const SizedBox(height: 8),
          DriverText(
            '${wide ? '${journey.trainNumber} · ' : ''}$_date\n$_route',
            style: driverText(context, 16, 22, secondary: true),
          ),
        ],
      ),
    );
    return [
      if (wide)
        _ReportSurface(
          surfaceKey: const Key('delivery-receipt'),
          wide: true,
          padding: 32,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: receipt,
          ),
        )
      else
        ...receipt,
      SizedBox(height: gap),
      if (wide)
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(child: contextCard),
            const SizedBox(width: 24),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (!_ack)
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          child: DriverAction(
                            label: hasDraft ? 'Check receipt' : 'Check status',
                            onPressed: busy ? null : onCheck,
                          ),
                        ),
                        const SizedBox(width: 24),
                        Expanded(
                          child: DriverAction(
                            label: hasDraft ? 'Retry same report' : 'Briefing',
                            outlined: true,
                            onPressed: busy
                                ? null
                                : hasDraft
                                ? onSend
                                : _briefing,
                          ),
                        ),
                      ],
                    ),
                  if (hasDraft) const SizedBox(height: 16),
                  if (hasDraft || _ack)
                    DriverAction(
                      label: 'Back to briefing',
                      outlined: hasDraft,
                      onPressed: _briefing,
                    ),
                ],
              ),
            ),
          ],
        )
      else
        contextCard,
      if (hasDraft) ...[
        SizedBox(height: gap),
        if (!wide) ...[
          DriverAction(
            label: 'Check receipt',
            outlined: true,
            phone: true,
            onPressed: busy ? null : onCheck,
          ),
          SizedBox(height: gap),
        ],
        _ReportGuidance(
          wide
              ? 'Urgent? Use the radio. Nothing sends automatically.'
              : 'Urgent? Use the radio. Retry when connected.',
          wide: wide,
        ),
      ] else if (!wide) ...[
        SizedBox(height: gap),
        DriverText(
          'Report ${report!.id} · $_reason',
          style: driverText(context, 16, 22, secondary: true),
        ),
      ],
    ];
  }

  Widget _timeline(BuildContext context, bool wide) {
    final steps = [
      if (hasDraft) ('Saved on this device', 'Saved', true),
      (
        'Server received',
        report == null ? 'Not confirmed' : reportTime(report!.receivedAt),
        report != null,
      ),
      (
        'Control acknowledged',
        _ack
            ? '${report!.acknowledgedByName} · ${wide ? driverTime(report!.acknowledgedAt!) : reportTime(report!.acknowledgedAt!)}'
            : hasDraft
            ? 'Not confirmed'
            : 'Not acknowledged',
        _ack,
      ),
    ];
    final cards = [
      for (final step in steps)
        _ReportStep(label: step.$1, value: step.$2, done: step.$3, wide: wide),
    ];
    return wide
        ? Row(
            key: const Key('receipt-timeline'),
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              for (var i = 0; i < cards.length; i++) ...[
                if (i > 0) const SizedBox(width: 24),
                Expanded(child: cards[i]),
              ],
            ],
          )
        : _ReportSurface(
            surfaceKey: const Key('receipt-timeline'),
            wide: false,
            padding: 32 / 3,
            child: Column(
              children: [
                for (var i = 0; i < cards.length; i++) ...[
                  if (i > 0) const SizedBox(height: 8),
                  cards[i],
                ],
              ],
            ),
          );
  }
}

String reportTime(DateTime value) {
  final local = value.toUtc().add(const Duration(hours: 5, minutes: 30));
  final hour = local.hour % 12 == 0 ? 12 : local.hour % 12;
  return '$hour:${local.minute.toString().padLeft(2, '0')} ${local.hour < 12 ? 'AM' : 'PM'}';
}

String reportDate(DateTime value) {
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
  return '${value.day} ${months[value.month - 1]} ${value.year}';
}

class _ReportSurface extends StatelessWidget {
  const _ReportSurface({
    required this.child,
    required this.wide,
    required this.padding,
    this.surfaceKey,
  });
  final Widget child;
  final bool wide;
  final double padding;
  final Key? surfaceKey;
  @override
  Widget build(BuildContext context) => Container(
    key: surfaceKey,
    padding: EdgeInsets.all(padding),
    decoration: BoxDecoration(
      color: SrTheme.of(context).palette.bgSurface,
      borderRadius: BorderRadius.circular(wide ? 28 : 44 / 3),
    ),
    child: child,
  );
}

class _ReportGuidance extends StatelessWidget {
  const _ReportGuidance(this.text, {required this.wide});
  final String text;
  final bool wide;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return Container(
      key: const Key('report-guidance'),
      padding: EdgeInsets.all(wide ? 16 : 32 / 3),
      decoration: BoxDecoration(
        color: p.statusWarningSoft,
        borderRadius: BorderRadius.circular(wide ? 28 : 44 / 3),
      ),
      child: Row(
        children: [
          DriverAsset(
            wide ? 'report-warning' : 'phone-report-warning',
            size: wide ? 28 : 24,
          ),
          SizedBox(width: wide ? 16 : 8),
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(right: wide ? 16 : 40 / 3),
              child: DriverText(
                text,
                style: driverText(
                  context,
                  20,
                  28,
                  weight: FontWeight.w500,
                  color: p.statusWarningInk,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _ReportReason extends StatelessWidget {
  const _ReportReason({
    required this.code,
    required this.label,
    required this.selected,
    required this.wide,
    this.onTap,
  });
  final String code, label;
  final bool selected, wide;
  final VoidCallback? onTap;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    final icon = switch (code) {
      'TRAIN_FAULT' => 'train',
      'SIGNAL_DELAY' => 'signal',
      'OBSTRUCTION' => 'obstruction',
      _ => 'stop',
    };
    final ink = selected ? p.accentPrimary : p.textSecondary;
    return Semantics(
      button: true,
      selected: selected,
      child: ConstrainedBox(
        constraints: BoxConstraints(minHeight: wide ? 112 : 64),
        child: Material(
          color: selected ? p.accentSoft : p.bgSurface,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(wide ? 28 : 44 / 3),
            side: BorderSide(
              color: selected ? p.accentPrimary : p.lineHairline,
              width: selected && wide ? 2 : 1,
            ),
          ),
          child: InkWell(
            onTap: onTap,
            borderRadius: BorderRadius.circular(wide ? 28 : 44 / 3),
            child: Padding(
              padding: EdgeInsets.symmetric(
                horizontal: wide ? (selected ? 26 : 25) : 17,
                vertical: wide ? 36 : 18,
              ),
              child: Row(
                mainAxisAlignment: wide
                    ? MainAxisAlignment.spaceBetween
                    : MainAxisAlignment.start,
                children: [
                  DriverAsset(
                    '${wide ? '' : 'phone-'}report-$icon',
                    key: ValueKey('reason-icon-$code'),
                    size: wide ? 40 : 28,
                    color: ink,
                  ),
                  if (!wide) const SizedBox(width: 32 / 3),
                  if (wide)
                    DriverText(
                      label,
                      style: driverText(
                        context,
                        22,
                        28,
                        weight: FontWeight.w500,
                      ),
                    )
                  else
                    Expanded(
                      child: DriverText(
                        label,
                        style: driverText(
                          context,
                          20,
                          28,
                          weight: FontWeight.w500,
                        ),
                      ),
                    ),
                  if (selected) ...[
                    if (!wide) const SizedBox(width: 32 / 3),
                    DriverAsset(
                      '${wide ? '' : 'phone-'}report-selected',
                      key: const Key('reason-selected-icon'),
                      size: wide ? 28 : 20,
                      color: ink,
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _ReportStep extends StatelessWidget {
  const _ReportStep({
    required this.label,
    required this.value,
    required this.done,
    required this.wide,
  });
  final String label, value;
  final bool done, wide;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    final icon = DriverAsset(
      '${wide ? '' : 'phone-'}report-${done ? 'check' : 'clock'}',
      size: wide ? 28 : 24,
      color: done ? p.statusSuccess : p.textSecondary,
    );
    final texts = [
      DriverText(label, style: driverText(context, 16, 22, secondary: true)),
      if (wide) const SizedBox(height: 8),
      DriverText(
        value,
        style: driverText(context, 20, 28, weight: FontWeight.w500),
      ),
    ];
    return wide
        ? Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: p.bgSurface2,
              borderRadius: BorderRadius.circular(20),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Align(alignment: Alignment.centerLeft, child: icon),
                const SizedBox(height: 8),
                ...texts,
              ],
            ),
          )
        : Row(
            children: [
              icon,
              const SizedBox(width: 8),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: texts,
                ),
              ),
            ],
          );
  }
}

class _ReportConnection extends StatelessWidget {
  const _ReportConnection({required this.uncertain, this.checkedAt});
  final bool uncertain;
  final DateTime? checkedAt;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return Row(
      children: [
        Container(
          constraints: const BoxConstraints(minWidth: 141.5),
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          decoration: BoxDecoration(
            color: p.bgSurface2,
            borderRadius: BorderRadius.circular(999),
          ),
          child: DriverText(
            'SIMULATED DATA',
            style: driverText(
              context,
              14,
              20,
              weight: FontWeight.w600,
              secondary: true,
            ),
          ),
        ),
        const SizedBox(width: 16),
        SizedBox(
          width: 180,
          child: Semantics(
            label: uncertain
                ? 'Receipt unknown'
                : 'Server last checked ${driverTime(checkedAt)}',
            child: Row(
              children: [
                if (uncertain || checkedAt != null) ...[
                  Container(
                    width: 8,
                    height: 8,
                    decoration: BoxDecoration(
                      shape: BoxShape.circle,
                      color: uncertain ? p.statusWarning : p.statusSuccess,
                    ),
                  ),
                  const SizedBox(width: 8),
                ],
                Expanded(
                  child: DriverText(
                    uncertain
                        ? 'Receipt unknown'
                        : checkedAt == null
                        ? 'Not checked'
                        : 'Live · ${driverTime(checkedAt)}',
                    style: driverText(
                      context,
                      16,
                      22,
                      color: uncertain ? p.statusWarningInk : p.textSecondary,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}
