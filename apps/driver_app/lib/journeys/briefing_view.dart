import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter/services.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../driver_frame.dart';
import 'briefing_api.dart';
import 'update_detail.dart';

/// Presentation of a selected dated journey. Reads, clock correction and cache policy
/// remain in DriverBriefingScreen; this view never changes server state.
class DriverDutyView extends StatefulWidget {
  const DriverDutyView({
    super.key,
    required this.journey,
    required this.briefing,
    required this.now,
    required this.displayName,
    this.saved = false,
    this.downloadedAt,
    this.checkedAt,
    this.notice,
    this.updatesOnly = false,
    this.onBack,
    this.onAccount,
    this.onReport,
    this.onReports,
    this.onRefresh,
    this.onReconnect,
  });
  final DriverJourney journey;
  final DriverBriefing briefing;
  final DateTime now;
  final String displayName;
  final bool saved, updatesOnly;
  final DateTime? downloadedAt, checkedAt;
  final String? notice;
  final VoidCallback? onBack, onAccount, onReport, onReports, onReconnect;
  final Future<void> Function()? onRefresh;

  @override
  State<DriverDutyView> createState() => _DriverDutyViewState();
}

class _DriverDutyViewState extends State<DriverDutyView> {
  late bool _updates = widget.updatesOnly;
  String? _selectedId;
  final _changeFocus = <String, FocusNode>{};
  bool _detailFromUpdates = false;

  FocusNode _focusFor(BriefingChange change) => _changeFocus.putIfAbsent(
    change.id!,
    () => FocusNode(debugLabel: 'Change ${change.id}'),
  );

  void _openChange(BriefingChange change) => setState(() {
    _detailFromUpdates = _updates;
    _selectedId = change.id;
  });

  void _closeDetail({required bool updates}) {
    final id = _selectedId;
    setState(() {
      _selectedId = null;
      _updates = updates;
    });
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _changeFocus[id]?.requestFocus();
    });
  }

  @override
  void dispose() {
    for (final node in _changeFocus.values) {
      node.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final b = widget.briefing;
    final evidence = freshnessOf(
      observedAt: b.observedAt,
      now: widget.now,
      ageingAfterSeconds: b.ageingAfterSeconds,
      unknownAfterSeconds: b.unknownAfterSeconds,
    );
    final running =
        !widget.saved &&
        evidence != SrEvidence.unknown &&
        b.availability != 'CANCELLED';
    final ordered = [...b.stops]
      ..sort((a, b) => a.sequence.compareTo(b.sequence));
    // A station observation is not a departure. Only an explicit segment identifies
    // the next stop; do not select one by comparing scheduled times to the clock.
    BriefingStop? next;
    final from = ordered.where((s) => s.id == b.positionFromStopId).firstOrNull;
    final to = ordered.where((s) => s.id == b.positionToStopId).firstOrNull;
    if (running &&
        from != null &&
        to != null &&
        from.sequence < to.sequence &&
        to.served &&
        (b.positionProgressPermille ?? -1) >= 0 &&
        (b.positionProgressPermille ?? 1001) <= 1000) {
      next = to;
    }
    final selected = b.changes
        .where((c) => c.id != null && c.id == _selectedId)
        .firstOrNull;
    final wide =
        MediaQuery.sizeOf(context).width >= 1000 &&
        MediaQuery.textScalerOf(context).scale(20) <= 28;
    final title = widget.saved
        ? wide
              ? 'Today’s duty · saved copy'
              : 'Saved briefing'
        : wide
        ? 'Today’s duty · ${driverDay(widget.journey.serviceDate)}'
        : 'Briefing';
    final connection = _Connection(
      observedAt: evidence == SrEvidence.current ? widget.now : b.observedAt,
      evidence: evidence,
      saved: widget.saved,
      wide: wide,
    );
    final report = widget.onReport == null || widget.saved
        ? null
        : DriverAction(
            key: const Key('report-action'),
            label: 'Report a problem',
            onPressed: widget.onReport,
            phone: !wide,
          );
    final notices = <Widget>[
      if (widget.notice != null)
        _Notice(widget.notice!, onRetry: widget.onRefresh),
      if (b.availability == 'CANCELLED')
        const _Notice('This journey is cancelled.'),
      if (b.availability == 'ENDS_EARLY')
        const _Notice(
          'This journey ends before its scheduled destination. Stops after its final station are not served.',
        ),
      if (evidence != SrEvidence.current && !widget.saved)
        FreshnessLine(
          observedAt: b.observedAt,
          now: widget.now,
          locale: 'en',
          ageingAfterSeconds: b.ageingAfterSeconds,
          unknownAfterSeconds: b.unknownAfterSeconds,
        ),
    ];
    final heading = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        DriverText(
          '${b.trainNumber} → ${widget.journey.destination}',
          key: const Key('duty-title'),
          style: driverText(
            context,
            wide ? 36 : 28,
            wide ? 44 : 36,
            weight: FontWeight.w600,
          ),
        ),
        SizedBox(height: wide ? 4 : 8 / 3),
        DriverText(
          '${widget.displayName} · Driver${wide ? '' : ' · ${driverDay(widget.journey.serviceDate)}'}',
          style: driverText(context, 16, 22, secondary: true),
        ),
      ],
    );
    final upcoming = next == null
        ? ordered
        : ordered.where((s) => s.sequence >= next!.sequence).toList();
    final stops = wide
        ? _Timetable(stops: ordered, next: next, briefing: b, running: running)
        : _Upcoming(stops: upcoming, next: next, running: running);
    final duty = wide
        ? LayoutBuilder(
            builder: (context, box) => SingleChildScrollView(
              physics: const AlwaysScrollableScrollPhysics(),
              key: const Key('driver-briefing'),
              padding: const EdgeInsets.fromLTRB(32, 24, 32, 32),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  ...notices,
                  ConstrainedBox(
                    constraints: BoxConstraints(minHeight: box.maxHeight - 56),
                    child: IntrinsicHeight(
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Expanded(
                            flex: 1232,
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                heading,
                                const SizedBox(height: 16),
                                _NextStop(
                                  stop: next,
                                  now: widget.now,
                                  wide: true,
                                ),
                                const SizedBox(height: 16),
                                stops,
                              ],
                            ),
                          ),
                          const SizedBox(width: 24),
                          Expanded(
                            flex: 880,
                            child: Padding(
                              padding: const EdgeInsets.only(bottom: 2),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.stretch,
                                children: [
                                  DriverText(
                                    'Confirmed changes',
                                    style: driverText(
                                      context,
                                      28,
                                      36,
                                      weight: FontWeight.w600,
                                    ),
                                  ),
                                  const SizedBox(height: 16),
                                  for (final c in b.changes) ...[
                                    Padding(
                                      padding: const EdgeInsets.only(right: 8),
                                      child: _ChangeCard(
                                        key: ValueKey('change-${c.id}'),
                                        change: c,
                                        briefing: b,
                                        running: running,
                                        focusNode: c.id == null
                                            ? null
                                            : _focusFor(c),
                                        onTap: c.id == null
                                            ? null
                                            : () => _openChange(c),
                                      ),
                                    ),
                                    const SizedBox(height: 16),
                                  ],
                                  if (b.changes.isEmpty)
                                    const _Notice(
                                      'No confirmed changes for this journey.',
                                    ),
                                  const Spacer(),
                                  ?report,
                                  const SizedBox(height: 12),
                                  _Safety(),
                                ],
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
          )
        : ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            key: const Key('driver-briefing'),
            padding: const EdgeInsets.fromLTRB(55 / 3, 16, 55 / 3, 8),
            children: [
              connection,
              const SizedBox(height: 8),
              ...notices,
              heading,
              const SizedBox(height: 8),
              _NextStop(stop: next, now: widget.now, wide: false),
              const SizedBox(height: 8),
              DriverText(
                'Upcoming stops',
                style: driverText(context, 22, 28, weight: FontWeight.w600),
              ),
              const SizedBox(height: 8 / 3),
              stops,
              if (b.changes.isNotEmpty) ...[
                const SizedBox(height: 8),
                _ChangeCard(
                  key: const Key('phone-change'),
                  change: b.changes.first,
                  briefing: b,
                  running: running,
                  compact: true,
                  focusNode: b.changes.first.id == null
                      ? null
                      : _focusFor(b.changes.first),
                  onTap: b.changes.first.id == null
                      ? null
                      : () => _openChange(b.changes.first),
                ),
              ],
              const SizedBox(height: 8),
              _Safety(),
            ],
          );
    final content = selected != null
        ? DriverUpdateDetail(
            change: selected,
            briefing: b,
            running: running,
            wide: wide,
            onAllUpdates: () => _closeDetail(updates: true),
            onBriefing: () => _closeDetail(updates: false),
          )
        : _updates || _selectedId != null
        ? _list(context, running)
        : widget.saved
        ? _SavedBriefing(
            briefing: b,
            destination: widget.journey.destination,
            downloadedAt: widget.downloadedAt,
            checkedAt: widget.checkedAt,
            wide: wide,
            notices: notices,
            onChange: _openChange,
            focusFor: _focusFor,
            action: _savedAction(wide),
          )
        : duty;
    final displayed =
        widget.saved && (selected != null || _updates || _selectedId != null)
        ? Column(
            children: [
              Padding(
                padding: EdgeInsets.fromLTRB(
                  wide ? 32 : 55 / 3,
                  16,
                  wide ? 32 : 55 / 3,
                  0,
                ),
                child: DriverText(
                  'Saved copy · read only · ${driverDay(widget.journey.serviceDate)} · downloaded ${driverTime(widget.downloadedAt)} · last checked ${driverTime(widget.checkedAt)}',
                  style: driverText(context, 16, 22, secondary: true),
                ),
              ),
              Expanded(child: content),
            ],
          )
        : content;
    return DriverFrame(
      title: selected != null
          ? wide
                ? 'Confirmed update'
                : 'Update'
          : _updates
          ? 'Updates'
          : title,
      displayName: widget.displayName,
      showReports: widget.onReports != null && !widget.saved,
      selected: _updates || selected != null ? 'updates' : 'journeys',
      now: widget.now,
      connection: widget.saved
          ? _SavedConnection(at: widget.downloadedAt)
          : connection,
      onPhoneBack: selected == null
          ? null
          : () => _closeDetail(updates: _detailFromUpdates),
      onSelect: (tab) {
        switch (tab) {
          case 'updates':
            setState(() {
              _updates = true;
              _selectedId = null;
            });
          case 'journeys':
            if (_updates || _selectedId != null) {
              setState(() {
                _updates = false;
                _selectedId = null;
              });
            } else {
              widget.onBack?.call();
            }
          case 'reports':
            widget.onReports?.call();
          case 'account':
            widget.onAccount?.call();
        }
      },
      phoneAction: selected != null
          ? DriverAction(
              label: 'Back to briefing',
              phone: true,
              outlined: true,
              onPressed: () => _closeDetail(updates: false),
            )
          : !_updates
          ? widget.saved
                ? _savedAction(false)
                : report
          : null,
      child: PopScope(
        canPop: _selectedId == null && !_updates,
        onPopInvokedWithResult: (didPop, result) {
          if (!didPop) {
            _closeDetail(updates: _selectedId != null && _detailFromUpdates);
          }
        },
        child: widget.onRefresh == null
            ? displayed
            : CallbackShortcuts(
                bindings: {
                  const SingleActivator(LogicalKeyboardKey.f5): () =>
                      unawaited(widget.onRefresh!()),
                },
                child: Semantics(
                  customSemanticsActions: {
                    CustomSemanticsAction(label: 'Refresh briefing'): () =>
                        unawaited(widget.onRefresh!()),
                  },
                  child: RefreshIndicator(
                    onRefresh: widget.onRefresh!,
                    child: displayed,
                  ),
                ),
              ),
      ),
    );
  }

  Widget _savedAction(bool wide) => DriverAction(
    key: const Key('saved-reconnect'),
    label: widget.onReconnect == null
        ? 'Retry connection'
        : 'Reconnect and sign in',
    phone: !wide,
    onPressed:
        widget.onReconnect ??
        (widget.onRefresh == null
            ? null
            : () => unawaited(widget.onRefresh!())),
  );

  Widget _list(BuildContext context, bool running) => ListView(
    physics: const AlwaysScrollableScrollPhysics(),
    padding: EdgeInsets.fromLTRB(
      MediaQuery.sizeOf(context).width >= 1000 ? 32 : 55 / 3,
      24,
      MediaQuery.sizeOf(context).width >= 1000 ? 32 : 55 / 3,
      32,
    ),
    key: const PageStorageKey('driver-updates'),
    children: [
      DriverText(
        'Updates from Control',
        key: const Key('updates-title'),
        style: driverText(context, 28, 36, weight: FontWeight.w600),
      ),
      const SizedBox(height: 8),
      DriverText(
        'Confirmed changes for ${widget.journey.trainNumber} → ${widget.journey.destination}',
        style: driverText(context, 20, 28, secondary: true),
      ),
      if (_selectedId != null)
        const _Notice(
          'This change is no longer in the current briefing. The list has been refreshed.',
        ),
      for (final c in widget.briefing.changes)
        Padding(
          padding: const EdgeInsets.only(top: 24),
          child: _ChangeCard(
            key: ValueKey('update-${c.id}'),
            change: c,
            briefing: widget.briefing,
            running: running,
            saved: widget.saved,
            savedAt: widget.downloadedAt,
            focusNode: c.id == null ? null : _focusFor(c),
            onTap: c.id == null ? null : () => _openChange(c),
          ),
        ),
      if (widget.briefing.changes.isEmpty)
        const _Notice('No confirmed changes for this journey.'),
      const SizedBox(height: 24),
      Align(
        alignment: Alignment.centerLeft,
        child: SizedBox(
          key: const Key('updates-back'),
          width: 480,
          child: DriverAction(
            label: 'Back to briefing',
            outlined: true,
            onPressed: () => _closeDetail(updates: false),
          ),
        ),
      ),
    ],
  );
}

class _SavedBriefing extends StatelessWidget {
  const _SavedBriefing({
    required this.briefing,
    required this.destination,
    required this.downloadedAt,
    required this.checkedAt,
    required this.wide,
    required this.notices,
    required this.onChange,
    required this.focusFor,
    required this.action,
  });
  final DriverBriefing briefing;
  final String destination;
  final DateTime? downloadedAt, checkedAt;
  final bool wide;
  final List<Widget> notices;
  final ValueChanged<BriefingChange> onChange;
  final FocusNode Function(BriefingChange) focusFor;
  final Widget action;

  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    final ordered = [...briefing.stops]
      ..sort((a, b) => a.sequence.compareTo(b.sequence));
    final from = ordered
        .where((s) => s.id == briefing.positionFromStopId)
        .firstOrNull;
    final to = ordered
        .where((s) => s.id == briefing.positionToStopId)
        .firstOrNull;
    // This is the stored segment, never a current next-stop claim. If the saved
    // segment is invalid, retain the whole snapshot instead of guessing by time.
    final stops =
        from != null &&
            to != null &&
            from.sequence < to.sequence &&
            to.served &&
            (briefing.positionProgressPermille ?? -1) >= 0 &&
            (briefing.positionProgressPermille ?? 1001) <= 1000
        ? ordered.where((s) => s.sequence >= to.sequence).toList()
        : ordered;
    final heading = Column(
      key: const Key('saved-service'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        DriverText(
          '${briefing.trainNumber} → $destination',
          style: driverText(
            context,
            wide ? 36 : 28,
            wide ? 44 : 36,
            weight: FontWeight.w600,
          ),
        ),
        SizedBox(height: wide ? 4 : 8 / 3),
        DriverText(
          'Saved copy · downloaded ${downloadedAt == null ? '—' : _confirmationTime(downloadedAt!)} · last checked ${checkedAt == null ? '—' : _confirmationTime(checkedAt!)}',
          key: const Key('saved-timestamps'),
          semanticsLabel:
              'Saved copy, read only. Downloaded ${downloadedAt == null ? 'unavailable' : _confirmationTime(downloadedAt!)}. Last successfully checked ${checkedAt == null ? 'unavailable' : _confirmationTime(checkedAt!)}.',
          style: driverText(context, 16, 22, secondary: true),
        ),
      ],
    );
    Widget change(BriefingChange c) => _ChangeCard(
      key: Key('saved-change-${c.id}'),
      change: c,
      briefing: briefing,
      running: false,
      compact: !wide,
      saved: true,
      savedAt: downloadedAt,
      focusNode: c.id == null ? null : focusFor(c),
      onTap: c.id == null ? null : () => onChange(c),
    );
    final gap = wide ? 24.0 : 32 / 3;
    final guidance = Container(
      key: const Key('saved-guidance'),
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
            child: DriverText(
              'Offline · saved ${driverTime(downloadedAt)}. Live times unavailable.${wide ? ' Urgent? Use the radio.' : ''}',
              style: driverText(
                context,
                20,
                28,
                weight: FontWeight.w500,
                color: p.statusWarningInk,
              ),
            ),
          ),
          SizedBox(width: wide ? 16 : 40 / 3),
        ],
      ),
    );
    return SingleChildScrollView(
      key: const Key('driver-briefing'),
      physics: const AlwaysScrollableScrollPhysics(),
      padding: EdgeInsets.fromLTRB(
        wide ? 32 : 55 / 3,
        wide ? 24 : 16,
        wide ? 32 : 55 / 3,
        32,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ...notices,
          guidance,
          SizedBox(height: gap),
          if (wide) ...[
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  flex: 1232,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      heading,
                      const SizedBox(height: 16),
                      _Timetable(
                        stops: stops,
                        next: null,
                        briefing: briefing,
                        running: false,
                        saved: true,
                        savedAt: downloadedAt,
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 24),
                Expanded(
                  flex: 880,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      DriverText(
                        'Saved changes',
                        style: driverText(
                          context,
                          28,
                          36,
                          weight: FontWeight.w600,
                        ),
                      ),
                      for (final c in briefing.changes) ...[
                        const SizedBox(height: 16),
                        Padding(
                          padding: const EdgeInsets.only(right: 8),
                          child: change(c),
                        ),
                      ],
                      if (briefing.changes.isEmpty)
                        DriverText(
                          'No changes in this saved briefing.',
                          style: driverText(context, 20, 28),
                        ),
                    ],
                  ),
                ),
              ],
            ),
            SizedBox(height: gap),
            Align(
              alignment: Alignment.centerLeft,
              child: SizedBox(width: 480, child: action),
            ),
          ] else ...[
            heading,
            SizedBox(height: gap),
            DriverText(
              'Saved estimates · not live',
              style: driverText(context, 22, 28, weight: FontWeight.w600),
            ),
            const SizedBox(height: 16 / 3),
            for (final s in stops) ...[
              Container(
                key: Key('stop-${s.id}'),
                padding: const EdgeInsets.all(8),
                decoration: BoxDecoration(
                  color: p.bgSurface,
                  borderRadius: BorderRadius.circular(44 / 3),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: DriverText(
                            s.station,
                            style: driverText(
                              context,
                              20,
                              28,
                              weight: FontWeight.w500,
                            ),
                          ),
                        ),
                        const SizedBox(width: 16 / 3),
                        DriverText(
                          s.served
                              ? driverTime(
                                  s.estimatedArrival ?? s.estimatedDeparture,
                                )
                              : '—',
                          semanticsLabel:
                              'Saved estimate ${s.served ? driverTime(s.estimatedArrival ?? s.estimatedDeparture) : 'unavailable'}, captured ${driverTime(downloadedAt)}, not live',
                          style: driverText(context, 28, 36),
                        ),
                      ],
                    ),
                    DriverText(
                      s.served
                          ? 'Sched. ${driverTime(s.scheduledArrival ?? s.scheduledDeparture)} · ${s.confirmedPlatform == null ? 'Planned platform' : 'Platform'} ${s.confirmedPlatform ?? s.plannedPlatform ?? '—'} · saved ${driverTime(downloadedAt)}'
                          : 'Not served in this saved briefing',
                      style: driverText(context, 16, 22, secondary: true),
                    ),
                  ],
                ),
              ),
              if (s != stops.last) const SizedBox(height: 16 / 3),
            ],
            if (briefing.changes.isNotEmpty) ...[
              SizedBox(height: gap),
              change(briefing.changes.first),
            ],
            SizedBox(height: gap),
            DriverText(
              'Urgent? Use the radio.',
              style: driverText(context, 20, 28, color: p.statusWarningInk),
            ),
          ],
        ],
      ),
    );
  }
}

class _SavedConnection extends StatelessWidget {
  const _SavedConnection({required this.at});
  final DateTime? at;
  @override
  Widget build(BuildContext context) => DriverConnection(
    label: 'Offline · saved ${driverTime(at)}',
    warning: true,
  );
}

class _Connection extends StatelessWidget {
  const _Connection({
    required this.observedAt,
    required this.evidence,
    required this.saved,
    required this.wide,
  });
  final DateTime? observedAt;
  final SrEvidence evidence;
  final bool saved, wide;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    final label = saved
        ? 'SAVED COPY'
        : evidence == SrEvidence.current
        ? 'Live · ${driverTime(observedAt)}'
        : evidence == SrEvidence.ageing
        ? 'Ageing · ${driverTime(observedAt)}'
        : 'Position unknown';
    Widget chip(String text, Color background, Color ink) => Container(
      key: ValueKey(text == 'SIMULATED DATA' ? 'simulation' : 'connection'),
      constraints: wide && text == 'SIMULATED DATA'
          ? const BoxConstraints(minWidth: 141.5)
          : null,
      padding: EdgeInsets.symmetric(
        horizontal: wide ? 12 : 16 / 3,
        vertical: wide ? 8 : 16 / 3,
      ),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(999),
      ),
      child: DriverText(
        text,
        style: driverText(context, 14, 20, weight: FontWeight.w600, color: ink),
      ),
    );
    return Wrap(
      spacing: wide ? 16 : 8,
      runSpacing: 8,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: wide
          ? [
              chip('SIMULATED DATA', p.bgSurface2, p.textSecondary),
              SizedBox(
                width: 180,
                child: Row(
                  children: [
                    if (evidence == SrEvidence.current && !saved) ...[
                      const DriverAsset('live', size: 8),
                      const SizedBox(width: 8),
                    ],
                    Expanded(
                      child: DriverText(
                        label,
                        style: driverText(context, 16, 22, secondary: true),
                      ),
                    ),
                  ],
                ),
              ),
            ]
          : [
              chip(
                label.toUpperCase(),
                evidence == SrEvidence.current && !saved
                    ? p.statusSuccessSoft
                    : p.bgSurface2,
                evidence == SrEvidence.current && !saved
                    ? p.statusSuccessInk
                    : p.textSecondary,
              ),
              chip('SIMULATED DATA', p.bgSurface2, p.textSecondary),
            ],
    );
  }
}

class _NextStop extends StatelessWidget {
  const _NextStop({required this.stop, required this.now, required this.wide});
  final BriefingStop? stop;
  final DateTime now;
  final bool wide;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette, s = stop;
    if (s == null) {
      return const _Notice(
        'Next stop unavailable. Scheduled stops are shown below.',
      );
    }
    final estimate = s.estimatedArrival ?? s.estimatedDeparture;
    final scheduled = s.scheduledArrival ?? s.scheduledDeparture;
    final delay = estimate == null || scheduled == null
        ? null
        : estimate.difference(scheduled).inMinutes;
    final late = delay == null
        ? 'Running time unavailable'
        : delay > 0
        ? '$delay min late'
        : 'On time';
    final title =
        '${s.station} · ${s.confirmedPlatform == null ? 'Planned platform' : 'Platform'} ${s.confirmedPlatform ?? s.plannedPlatform ?? '—'}';
    final labels = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        DriverText(
          'NEXT STOP',
          style: driverText(
            context,
            14,
            20,
            weight: FontWeight.w600,
            color: p.statusWarningInk,
          ),
        ),
        SizedBox(height: wide ? 8 : 16 / 3),
        DriverText(
          title,
          style: driverText(
            context,
            wide ? 28 : 22,
            wide ? 36 : 28,
            weight: FontWeight.w600,
          ),
        ),
        SizedBox(height: wide ? 8 : 16 / 3),
        if (wide)
          DriverText(
            'Exp. ${driverTime(estimate)} · $late',
            style: driverText(
              context,
              20,
              28,
              weight: FontWeight.w500,
              color: p.statusWarningInk,
            ),
          )
        else
          SizedBox(
            width: double.infinity,
            child: Wrap(
              alignment: WrapAlignment.spaceBetween,
              spacing: 16,
              children: [
                DriverText(
                  'Exp. ${driverTime(estimate)}',
                  style: driverText(context, 28, 36),
                ),
                DriverText(
                  late,
                  style: driverText(context, 16, 22, color: p.statusWarningInk),
                ),
              ],
            ),
          ),
      ],
    );
    return Container(
      key: const Key('next-stop'),
      padding: EdgeInsets.all(wide ? 24 : 40 / 3),
      decoration: BoxDecoration(
        color: p.statusWarningSoft,
        borderRadius: BorderRadius.circular(wide ? 28 : 44 / 3),
      ),
      child: wide
          ? Row(
              children: [
                Expanded(child: labels),
                const SizedBox(width: 24),
                SizedBox(
                  width: 144,
                  child: Column(
                    children: [
                      DriverText(
                        estimate == null
                            ? '—'
                            : '${estimate.difference(now).inMinutes.clamp(0, 999)}',
                        style: driverText(
                          context,
                          64,
                          76,
                          weight: FontWeight.w300,
                        ),
                      ),
                      DriverText(
                        'min to next stop',
                        style: driverText(context, 16, 22, secondary: true),
                      ),
                    ],
                  ),
                ),
              ],
            )
          : labels,
    );
  }
}

class _Timetable extends StatelessWidget {
  const _Timetable({
    required this.stops,
    required this.next,
    required this.briefing,
    required this.running,
    this.saved = false,
    this.savedAt,
  });
  final List<BriefingStop> stops;
  final BriefingStop? next;
  final DriverBriefing briefing;
  final bool running;
  final bool saved;
  final DateTime? savedAt;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    Widget cells(List<Widget> values) => Row(
      children: [
        Expanded(child: values[0]),
        const SizedBox(width: 12),
        SizedBox(width: 80, child: values[1]),
        const SizedBox(width: 12),
        SizedBox(width: 164, child: values[2]),
        const SizedBox(width: 12),
        SizedBox(width: 52, child: values[3]),
        const SizedBox(width: 80),
      ],
    );
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            DriverText(
              'Stops',
              style: driverText(context, 28, 36, weight: FontWeight.w600),
            ),
            DriverText(
              saved
                  ? 'Saved ${driverTime(savedAt)} · not live'
                  : 'Scheduled / expected',
              style: driverText(context, 16, 22, secondary: true),
            ),
          ],
        ),
        const SizedBox(height: 8),
        Padding(
          padding: const EdgeInsets.all(12),
          child: cells([
            for (final s in [
              'STATION',
              'SCHED.',
              saved ? 'SAVED EST.' : 'EXPECTED',
              'PLAT.',
            ])
              DriverText(
                s,
                style: driverText(
                  context,
                  14,
                  20,
                  weight: FontWeight.w600,
                  secondary: true,
                ),
              ),
          ]),
        ),
        const SizedBox(height: 8),
        for (final s in stops) ...[
          Container(
            key: Key('stop-${s.id}'),
            constraints: const BoxConstraints(minHeight: 72),
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: s.id == next?.id ? p.accentSoft : Colors.transparent,
              borderRadius: BorderRadius.circular(20),
            ),
            child: cells([
              DriverText(
                s.station,
                style: driverText(context, 20, 28, weight: FontWeight.w500),
              ),
              DriverText(
                driverTime(s.scheduledArrival ?? s.scheduledDeparture),
                style: driverText(context, 16, 22, secondary: true),
              ),
              if (!s.served)
                DriverText(
                  'Not served on this journey',
                  style: driverText(context, 16, 22, color: p.statusDangerInk),
                )
              else if (saved)
                DriverText(
                  '${driverTime(s.estimatedArrival ?? s.estimatedDeparture)} at ${driverTime(savedAt)}',
                  semanticsLabel:
                      'Saved estimate ${driverTime(s.estimatedArrival ?? s.estimatedDeparture)}, captured ${driverTime(savedAt)}, not live',
                  style: driverText(context, 16, 22, secondary: true),
                )
              else if (s.stationId != null &&
                  s.stationId == briefing.lastKnownStationId)
                DriverText(
                  briefing.observedAt == null
                      ? '—'
                      : 'Last observed ${driverTime(briefing.observedAt)}',
                  style: driverText(context, 16, 22, secondary: true),
                )
              else
                DriverText(
                  running
                      ? driverTime(s.estimatedArrival ?? s.estimatedDeparture)
                      : '—',
                  style: driverText(
                    context,
                    28,
                    36,
                    color: s.id == next?.id
                        ? p.textPrimary
                        : p.statusWarningInk,
                  ),
                ),
              Semantics(
                label: s.confirmedPlatform == null
                    ? 'Planned platform ${s.plannedPlatform ?? 'unavailable'}'
                    : 'Confirmed platform ${s.confirmedPlatform}',
                child: DriverText(
                  s.confirmedPlatform ?? s.plannedPlatform ?? '—',
                  style: driverText(
                    context,
                    20,
                    28,
                    weight: FontWeight.w500,
                    secondary: s.id != next?.id,
                  ),
                ),
              ),
            ]),
          ),
          if (s != stops.last) const SizedBox(height: 8),
        ],
      ],
    );
  }
}

class _Upcoming extends StatelessWidget {
  const _Upcoming({
    required this.stops,
    required this.next,
    required this.running,
  });
  final List<BriefingStop> stops;
  final BriefingStop? next;
  final bool running;
  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    return Column(
      children: [
        for (final s in stops)
          Padding(
            padding: EdgeInsets.only(bottom: s == stops.last ? 0 : 8 / 3),
            child: Container(
              key: Key('stop-${s.id}'),
              padding: const EdgeInsets.symmetric(
                horizontal: 8,
                vertical: 16 / 3,
              ),
              decoration: BoxDecoration(
                color: s.id == next?.id ? p.accentSoft : p.bgSurface,
                borderRadius: BorderRadius.circular(44 / 3),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: DriverText(
                          s.station,
                          style: driverText(
                            context,
                            20,
                            28,
                            weight: FontWeight.w500,
                          ),
                        ),
                      ),
                      const SizedBox(width: 8),
                      DriverText(
                        running && s.served
                            ? driverTime(
                                s.estimatedArrival ?? s.estimatedDeparture,
                              )
                            : '—',
                        style: driverText(context, 28, 36),
                      ),
                    ],
                  ),
                  DriverText(
                    s.served
                        ? 'Sched. ${driverTime(s.scheduledArrival ?? s.scheduledDeparture)} · ${s.confirmedPlatform == null ? 'Planned platform' : 'Platform'} ${s.confirmedPlatform ?? s.plannedPlatform ?? '—'}'
                        : 'Not served on this journey',
                    semanticsLabel: s.served
                        ? 'Scheduled ${driverTime(s.scheduledArrival ?? s.scheduledDeparture)}, ${s.confirmedPlatform == null ? 'Planned platform ${s.plannedPlatform ?? 'unavailable'}' : 'Confirmed platform ${s.confirmedPlatform}'}'
                        : 'Not served on this journey',
                    style: driverText(context, 16, 22, secondary: true),
                  ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}

String _confirmationTime(DateTime value) {
  final local = value.toUtc().add(const Duration(hours: 5, minutes: 30));
  final hour = local.hour % 12;
  return '${hour == 0 ? 12 : hour}:${local.minute.toString().padLeft(2, '0')} ${local.hour < 12 ? 'AM' : 'PM'}';
}

class _ChangeCard extends StatelessWidget {
  const _ChangeCard({
    super.key,
    required this.change,
    required this.briefing,
    required this.running,
    this.onTap,
    this.compact = false,
    this.focusNode,
    this.saved = false,
    this.savedAt,
  });
  final BriefingChange change;
  final DriverBriefing briefing;
  final bool running, compact;
  final VoidCallback? onTap;
  final FocusNode? focusNode;
  final bool saved;
  final DateTime? savedAt;
  @override
  Widget build(BuildContext context) {
    final c = change, p = SrTheme.of(context).palette;
    final target = briefing.stops
        .where((s) => s.id == c.targetStopId)
        .firstOrNull;
    final name = target?.station ?? 'Station unavailable';
    final kind = switch (c.kind) {
      'PLATFORM' => 'PLATFORM CHANGE',
      'DELAY' => saved ? 'DELAY' : 'DELAY · ${c.delayMinutes ?? '—'} MIN',
      'CANCEL' => 'CANCELLATION',
      'EARLY_END' => 'ENDS EARLY',
      _ => 'CONFIRMED CHANGE',
    };
    final title = switch (c.kind) {
      'PLATFORM' =>
        compact
            ? '$name: ${target?.plannedPlatform ?? '—'} → ${c.newPlatform ?? '—'}'
            : '$name: Platform ${c.newPlatform ?? '—'}',
      'DELAY' =>
        saved
            ? 'Saved delay: ${c.delayMinutes ?? '—'} min'
            : running
            ? 'Running ${c.delayMinutes ?? '—'} min late'
            : 'Confirmed ${c.delayMinutes ?? '—'} min delay',
      'CANCEL' => 'This journey is cancelled',
      'EARLY_END' => 'This journey ends early',
      _ => 'Service change',
    };
    final by = c.confirmedBy ?? 'Name unavailable';
    final desc = saved && c.kind == 'PLATFORM'
        ? 'Was ${target?.plannedPlatform ?? '—'} · $name ${driverTime(target?.estimatedArrival ?? target?.estimatedDeparture)} at ${driverTime(savedAt)}'
        : saved && c.kind == 'DELAY'
        ? '${c.delayMinutes ?? '—'} min late at ${driverTime(c.confirmedAt)} · saved'
        : c.kind == 'PLATFORM'
        ? 'Was ${target?.plannedPlatform ?? '—'} · ${briefing.trainNumber}${running && target != null ? ' · expected ${driverTime(target.estimatedArrival ?? target.estimatedDeparture)}' : ''}'
        : c.kind == 'DELAY' && target != null && running
        ? '$name ${driverTime(target.scheduledArrival ?? target.scheduledDeparture)} → ${driverTime(target.estimatedArrival ?? target.estimatedDeparture)}'
        : 'Confirmed for this dated journey';
    return Material(
      color: p.bgSurface,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(compact ? 44 / 3 : 28),
        side: compact ? BorderSide.none : BorderSide(color: p.lineHairline),
      ),
      child: InkWell(
        onTap: onTap,
        focusNode: focusNode,
        borderRadius: BorderRadius.circular(compact ? 44 / 3 : 28),
        child: Padding(
          padding: EdgeInsets.all(compact ? 32 / 3 : 21),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Row(
                children: [
                  Expanded(
                    child: DriverText(
                      saved
                          ? compact
                                ? 'SAVED $kind'
                                : '$kind · SAVED'
                          : compact
                          ? '$kind · ${by.toUpperCase()} ${driverTime(c.confirmedAt)}'
                          : kind,
                      style: driverText(
                        context,
                        14,
                        20,
                        weight: FontWeight.w600,
                        color: c.kind == 'DELAY'
                            ? p.statusWarningInk
                            : p.textPrimary,
                      ),
                    ),
                  ),
                  if (!compact) const DriverAsset('chevron', size: 24),
                ],
              ),
              SizedBox(height: compact ? 8 / 3 : 8),
              Row(
                children: [
                  Expanded(
                    child: DriverText(
                      title,
                      style: driverText(
                        context,
                        compact ? 20 : 22,
                        28,
                        weight: FontWeight.w500,
                      ),
                    ),
                  ),
                  if (compact && !saved) ...[
                    const SizedBox(width: 16 / 3),
                    const DriverAsset(
                      'phone-chevron',
                      key: Key('phone-change-chevron'),
                      size: 24,
                    ),
                    const SizedBox(width: 8),
                  ],
                ],
              ),
              if (!compact) ...[
                const SizedBox(height: 8),
                DriverText(
                  desc,
                  style: driverText(context, 16, 22, secondary: true),
                ),
                const SizedBox(height: 8),
                DriverText(
                  saved
                      ? '$by · ${driverTime(c.confirmedAt)} · saved'
                      : '$by · ${_confirmationTime(c.confirmedAt)}',
                  style: driverText(context, 16, 22, secondary: true),
                ),
              ],
              if (compact && saved) ...[
                const SizedBox(height: 8 / 3),
                DriverText(
                  '$by · ${driverTime(c.confirmedAt)} · saved ${driverTime(savedAt)}',
                  style: driverText(context, 16, 22, secondary: true),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _Safety extends StatelessWidget {
  @override
  Widget build(BuildContext context) => DriverText(
    'Info only — follow signals.',
    key: const Key('safety'),
    style: driverText(
      context,
      MediaQuery.sizeOf(context).width < 1000 ? 14 : 16,
      MediaQuery.sizeOf(context).width < 1000 ? 20 : 22,
      weight: MediaQuery.sizeOf(context).width < 1000
          ? FontWeight.w600
          : FontWeight.w400,
      secondary: true,
    ),
  );
}

class _Notice extends StatelessWidget {
  const _Notice(this.message, {this.onRetry});
  final String message;
  final VoidCallback? onRetry;
  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    child: Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          DriverText(message, style: driverText(context, 20, 28)),
          if (onRetry != null)
            TextButton(onPressed: onRetry, child: DriverText('Try again')),
        ],
      ),
    ),
  );
}
