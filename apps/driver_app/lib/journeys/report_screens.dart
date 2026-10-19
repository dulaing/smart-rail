import 'dart:async';

import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'briefing_api.dart';
import 'report_api.dart';
import 'report_drafts.dart';

class DriverReportsScreen extends StatefulWidget {
  const DriverReportsScreen({
    super.key,
    required this.journeyApi,
    required this.reportApi,
  });
  final DriverBriefingApi journeyApi;
  final DriverReportApi reportApi;

  @override
  State<DriverReportsScreen> createState() => _DriverReportsScreenState();
}

class _DriverReportsScreenState extends State<DriverReportsScreen> {
  late final Future<DriverRead<List<DriverJourney>>> _journeys = widget
      .journeyApi
      .journeys(reportToday());

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    return FutureBuilder(
      future: _journeys,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        final result = snapshot.data;
        if (result is! DriverReadOk<List<DriverJourney>>) {
          return const Text(
            'Could not load your assigned journeys. Reconnect and try again.',
          );
        }
        if (result.value.isEmpty) {
          return const Text('No assigned journeys for this service date.');
        }
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Choose a journey for your report.',
              style: theme.text('body'),
            ),
            for (final journey in result.value)
              Card(
                child: ListTile(
                  minTileHeight: theme.metrics.minTarget,
                  title: Text('${journey.trainNumber} · ${journey.label}'),
                  subtitle: Text('${journey.origin} → ${journey.destination}'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => DriverReportJourneyScreen(
                        journey: journey,
                        api: widget.reportApi,
                        journeyApi: widget.journeyApi,
                      ),
                    ),
                  ),
                ),
              ),
          ],
        );
      },
    );
  }
}

class DriverReportJourneyScreen extends StatefulWidget {
  const DriverReportJourneyScreen({
    super.key,
    required this.journey,
    required this.api,
    required this.journeyApi,
  });
  final DriverJourney journey;
  final DriverReportApi api;
  final DriverBriefingApi journeyApi;

  @override
  State<DriverReportJourneyScreen> createState() =>
      _DriverReportJourneyScreenState();
}

class _DriverReportJourneyScreenState extends State<DriverReportJourneyScreen>
    with WidgetsBindingObserver {
  static const _reasons = {
    'TRAIN_FAULT': 'Train fault',
    'SIGNAL_DELAY': 'Signal delay',
    'OBSTRUCTION': 'Obstruction',
    'CANNOT_CONTINUE': 'Cannot continue',
  };
  final ReportDraftStore _drafts = ReportDraftStore();
  List<DriverReport> _reports = [];
  ReportDraft? _draft;
  String? _reason = 'TRAIN_FAULT';
  String? _message;
  String? _draftState;
  bool _accessLost = false;
  bool _busy = true;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(_load());
    _poll = Timer.periodic(
      const Duration(seconds: 30),
      (_) => unawaited(_refreshReports()),
    );
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _poll?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(_refreshReports());
  }

  Future<void> _refreshReports() async {
    if (_busy || _accessLost) return;
    final result = await widget.api.list(widget.journey.id);
    if (!mounted) return;
    if (result case ReportListOk(:final reports)) {
      setState(() => _reports = reports);
    } else if (result case ReportFailure(status: 401 || 403)) {
      setState(() => _accessLost = true);
    }
  }

  Future<void> _load() async {
    final owner = await widget.journeyApi.ownerId();
    if (owner == null) {
      if (mounted) {
        setState(() {
          _busy = false;
          _message = 'Sign in again to use reports.';
        });
      }
      return;
    }
    final saved = await _drafts.read(owner, widget.journey.id);
    final result = await widget.api.list(widget.journey.id);
    DriverReport? recovered;
    final savedDraft = saved;
    if (savedDraft != null) {
      if (result case ReportListOk(:final reports)) {
        final inPage = reports.where(
          (report) => report.clientReportId == savedDraft.clientReportId,
        );
        if (inPage.isNotEmpty) {
          recovered = inPage.first;
        } else {
          final lookup = await widget.api.findByClientId(
            widget.journey.id,
            savedDraft.clientReportId,
          );
          if (lookup case ReportListOk(:final reports)
              when reports.isNotEmpty) {
            recovered = reports.first;
          }
        }
      }
    }
    if (!mounted) return;
    setState(() {
      _draft = recovered == null ? saved : null;
      if (savedDraft != null && recovered == null) {
        _reason = savedDraft.reasonCode;
        _draftState = 'Receipt not confirmed. Check receipt before retrying.';
      }
      if (recovered != null) {
        _reports = [recovered, ..._reports];
      }
      if (result case ReportListOk(:final reports)) {
        _reports = [
          ?recovered,
          ...reports.where((report) => report.id != recovered?.id),
        ];
        _message = null;
        _accessLost = false;
      } else if (result case ReportFailure(:final message, :final status)) {
        _message = message;
        _accessLost = status == 401 || status == 403;
      }
      _busy = false;
    });
  }

  Future<void> _send({bool retry = false}) async {
    if (_busy || _reason == null) return;
    setState(() {
      _busy = true;
      _message = null;
      _draftState = 'Sending report…';
    });
    final owner = await widget.journeyApi.ownerId();
    if (owner == null) {
      setState(() {
        _busy = false;
        _draftState = 'Not sent — sign in again.';
      });
      return;
    }
    final draft = retry && _draft != null
        ? _draft!
        : await _drafts.create(owner, widget.journey.id, _reason!);
    setState(() {
      _draft = draft;
      _draftState = 'Not sent — Control has not been told.';
    });
    final result = await widget.api.submit(
      widget.journey.id,
      draft.clientReportId,
      draft.reasonCode,
    );
    if (!mounted) return;
    switch (result) {
      case ReportOk(:final report):
        await _drafts.clear(widget.journey.id);
        setState(() {
          _draft = null;
          _accessLost = false;
          _draftState = null;
          _reports = [
            report,
            ..._reports.where((item) => item.id != report.id),
          ];
          _busy = false;
        });
      case ReportFailure(status: null):
        setState(() {
          _draftState = 'Receipt not confirmed. Reconcile or retry with the same report key.';
          _busy = false;
        });
      case ReportFailure(:final status, :final message, :final code):
        setState(() {
          _accessLost = status == 401 || status == 403;
          _draftState = code == 'CLIENT_REPORT_ID_IN_USE'
              ? 'Report key conflict. Reconcile with Control before creating another report.'
              : status == 401 || status == 403
              ? 'Access ended. Sign in again or contact Control.'
              : 'Report not confirmed.';
          _message = message;
          _busy = false;
        });
      case ReportListOk():
        setState(() {
          _busy = false;
        });
    }
  }

  Future<void> _reconcile() async {
    final draft = _draft;
    if (_busy || draft == null) return;
    setState(() {
      _busy = true;
      _draftState = 'Checking for server receipt…';
    });
    final result = await widget.api.findByClientId(
      widget.journey.id,
      draft.clientReportId,
    );
    if (!mounted) return;
    switch (result) {
      case ReportListOk(:final reports) when reports.isNotEmpty:
        await _drafts.clear(widget.journey.id);
        setState(() {
          _reports = [
            reports.first,
            ..._reports.where((item) => item.id != reports.first.id),
          ];
          _draft = null;
          _draftState = null;
          _busy = false;
        });
      case ReportListOk():
        setState(() {
          _draftState =
              'No receipt found yet. The original request may still finish.';
          _busy = false;
        });
      case ReportFailure(status: null):
        setState(() {
          _draftState = 'Receipt not confirmed. Reconnect and reconcile again.';
          _busy = false;
        });
      case ReportFailure(:final status, :final message):
        setState(() {
          _accessLost = status == 401 || status == 403;
          _draftState = status == 401 || status == 403
              ? 'Access ended. Sign in again or contact Control.'
              : message;
          _busy = false;
        });
      case ReportOk():
        setState(() {
          _busy = false;
        });
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    return Scaffold(
      appBar: AppBar(title: Text('Report · ${widget.journey.trainNumber}')),
      body: ListView(
        padding: EdgeInsets.all(gap),
        children: [
          const Text(
            'Send reports only while stopped. For urgent danger, use the established radio or emergency procedure; do not wait for this app.',
          ),
          SizedBox(height: gap),
          Text('Choose a reason', style: theme.text('heading')),
          AbsorbPointer(
            absorbing: _busy || _draft != null || _accessLost,
            child: RadioGroup<String>(
              groupValue: _reason,
              onChanged: (value) {
                if (value != null && !_busy && _draft == null) {
                  setState(() => _reason = value);
                }
              },
              child: Column(
                children: [
                  for (final entry in _reasons.entries)
                    RadioListTile<String>(
                      value: entry.key,
                      title: Text(entry.value, style: theme.text('body')),
                      minTileHeight: theme.metrics.minTarget,
                      contentPadding: EdgeInsets.zero,
                    ),
                ],
              ),
            ),
          ),
          FilledButton(
            onPressed: _busy || _accessLost
                ? null
                : () => unawaited(_send(retry: _draft != null)),
            child: Text(
              _draft == null ? 'Review and send' : 'Retry same report',
            ),
          ),
          if (_draftState != null) ...[
            SizedBox(height: gap),
            Semantics(
              liveRegion: true,
              child: Text(_draftState!, key: const Key('report-draft-state')),
            ),
            if (_draft != null)
              TextButton(
                onPressed: _busy || _accessLost ? null : _reconcile,
                child: const Text('Check receipt'),
              ),
          ],
          if (_message != null) ...[SizedBox(height: gap), Text(_message!)],
          if (_busy) const LinearProgressIndicator(),
          SizedBox(height: gap),
          Text('Your reports', style: theme.text('heading')),
          for (final report in _reports)
            Card(
              child: Padding(
                padding: EdgeInsets.all(gap),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      _reasons[report.reasonCode] ?? report.reasonCode,
                      style: theme.text('body'),
                    ),
                    SizedBox(height: gap / 2),
                    Text(
                      'Received by server · ${_time(report.receivedAt)}',
                      style: theme.text('body'),
                    ),
                    SizedBox(height: gap / 2),
                    Text(
                      report.state == 'ACKNOWLEDGED' &&
                              report.acknowledgedByName != null &&
                              report.acknowledgedAt != null
                          ? 'Acknowledged by ${report.acknowledgedByName} · ${_time(report.acknowledgedAt)}'
                          : 'Sent · awaiting Control acknowledgement',
                      style: theme.text('body'),
                    ),
                  ],
                ),
              ),
            ),
        ],
      ),
    );
  }
}

String _time(DateTime? value) {
  if (value == null) return 'time unavailable';
  final local = value.toLocal();
  return '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
}

const _colomboOffset = Duration(hours: 5, minutes: 30);
DateTime reportToday([DateTime? now]) {
  final local = (now ?? DateTime.now()).toUtc().add(_colomboOffset);
  return DateTime.utc(local.year, local.month, local.day);
}
