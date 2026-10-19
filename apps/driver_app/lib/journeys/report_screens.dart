import 'dart:async';

import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'briefing_api.dart';
import 'report_api.dart';
import 'report_drafts.dart';
import 'report_view.dart';

export 'report_view.dart' show reportReasons;

class DriverReportsScreen extends StatefulWidget {
  DriverReportsScreen({
    super.key,
    required this.journeyApi,
    required this.reportApi,
    ReportDraftStore? drafts,
    this.now,
    this.displayName = 'Driver',
    this.onAccount,
    this.onBriefing,
    this.onUpdates,
    this.canCreate = true,
  }) : drafts = drafts ?? ReportDraftStore();
  final DriverBriefingApi journeyApi;
  final DriverReportApi reportApi;
  final ReportDraftStore drafts;

  /// The clock, so a test can put it either side of midnight.
  final DateTime Function()? now;
  final String displayName;
  final VoidCallback? onAccount;
  final ValueChanged<DriverJourney>? onBriefing, onUpdates;
  final bool canCreate;

  @override
  State<DriverReportsScreen> createState() => _DriverReportsScreenState();
}

/// What the Reports list shows: today's assigned journeys, and every report whose delivery is
/// not confirmed. The second part comes from this device, so it does not depend on the date or
/// on the network: a report written before midnight stays reachable after it.
class _ReportsHome {
  const _ReportsHome(this.today, this.pending);
  final DriverRead<List<DriverJourney>> today;
  final List<ReportDraft> pending;
}

class _DriverReportsScreenState extends State<DriverReportsScreen> {
  late Future<_ReportsHome> _home = _loadHome();

  Future<_ReportsHome> _loadHome() async {
    final owner = await widget.journeyApi.ownerId();
    final pending = owner == null
        ? <ReportDraft>[]
        : await widget.drafts.pendingFor(owner);
    final today = await widget.journeyApi.journeys(
      reportToday(widget.now?.call()),
    );
    return _ReportsHome(today, pending);
  }

  /// A report may have been sent, reconciled or started while the other screen was open.
  void _reload() {
    if (!mounted) return;
    final next = _loadHome();
    setState(() {
      _home = next;
    });
  }

  void _open(DriverJourney journey) => Navigator.of(context)
      .push(
        MaterialPageRoute<void>(
          builder: (_) => DriverReportJourneyScreen(
            journey: journey,
            api: widget.reportApi,
            journeyApi: widget.journeyApi,
            drafts: widget.drafts,
            displayName: widget.displayName,
            onAccount: widget.onAccount,
            history: true,
            canCreate: widget.canCreate,
            onBriefing: widget.onBriefing == null
                ? null
                : () => widget.onBriefing!(journey),
            onUpdates: widget.onUpdates == null
                ? null
                : () => widget.onUpdates!(journey),
          ),
        ),
      )
      .then((_) => _reload());

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    return FutureBuilder(
      future: _home,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: CircularProgressIndicator());
        }
        final home = snapshot.data;
        if (home == null) {
          return const Text('Could not load your reports. Try again.');
        }
        final today = home.today;
        final pendingIds = {for (final draft in home.pending) draft.journeyId};
        // A journey with an unconfirmed report is listed once, in the section that says so.
        final choices = today is DriverReadOk<List<DriverJourney>>
            ? [
                for (final journey in today.value)
                  if (!pendingIds.contains(journey.id)) journey,
              ]
            : <DriverJourney>[];
        return Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (home.pending.isNotEmpty) ...[
              Text('Reports not confirmed', style: theme.text('heading')),
              Text(
                'Control may not have received these. Open one to check receipt or retry the same report. Nothing is sent automatically.',
                style: theme.text('body'),
              ),
              for (final draft in home.pending)
                Card(
                  child: ListTile(
                    key: Key('pending-${draft.journeyId}'),
                    minTileHeight: theme.metrics.minTarget,
                    title: Text(_pendingTitle(draft)),
                    subtitle: Text(
                      '${reportReasons[draft.reasonCode] ?? draft.reasonCode} · Receipt not confirmed',
                    ),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => _open(draft.journey ?? _unknownJourney(draft)),
                  ),
                ),
            ],
            if (today is! DriverReadOk<List<DriverJourney>>)
              const Text(
                'Could not load your assigned journeys. Reconnect and try again.',
              )
            else if (choices.isEmpty && home.pending.isEmpty)
              const Text('No assigned journeys for this service date.')
            else if (choices.isNotEmpty) ...[
              Text(
                'Choose a journey for your report.',
                style: theme.text('body'),
              ),
              for (final journey in choices)
                Card(
                  child: ListTile(
                    minTileHeight: theme.metrics.minTarget,
                    title: Text('${journey.trainNumber} · ${journey.label}'),
                    subtitle: Text(
                      '${journey.origin} → ${journey.destination}',
                    ),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => _open(journey),
                  ),
                ),
            ],
          ],
        );
      },
    );
  }
}

String _pendingTitle(ReportDraft draft) {
  final journey = draft.journey;
  if (journey == null) return 'Earlier journey';
  return '${journey.trainNumber} · ${_date(journey.serviceDate)}';
}

String _date(DateTime value) =>
    '${value.year.toString().padLeft(4, '0')}-${value.month.toString().padLeft(2, '0')}-${value.day.toString().padLeft(2, '0')}';

/// Stands in for a journey whose details an older version of the app did not save with its draft.
/// The report itself needs only the journey ID, which every draft has.
DriverJourney _unknownJourney(ReportDraft draft) => DriverJourney(
  id: draft.journeyId,
  serviceDate: DateTime.utc(1970),
  trainNumber: 'earlier journey',
  label: 'details not saved',
  origin: '',
  departure: null,
  destination: '',
  arrival: null,
  availability: 'AS_SCHEDULED',
);

class DriverReportJourneyScreen extends StatefulWidget {
  DriverReportJourneyScreen({
    super.key,
    required this.journey,
    required this.api,
    required this.journeyApi,
    ReportDraftStore? drafts,
    this.displayName = 'Driver',
    this.onAccount,
    this.history = false,
    this.onBriefing,
    this.onUpdates,
    this.canCreate = true,
  }) : drafts = drafts ?? ReportDraftStore();
  final DriverJourney journey;
  final DriverReportApi api;
  final DriverBriefingApi journeyApi;
  final ReportDraftStore drafts;
  final String displayName;
  final VoidCallback? onAccount;
  final bool history;
  final VoidCallback? onBriefing, onUpdates;
  final bool canCreate;

  @override
  State<DriverReportJourneyScreen> createState() =>
      _DriverReportJourneyScreenState();
}

class _DriverReportJourneyScreenState extends State<DriverReportJourneyScreen>
    with WidgetsBindingObserver {
  ReportDraftStore get _drafts => widget.drafts;
  List<DriverReport> _reports = [];
  ReportDraft? _draft;
  String? _reason = 'TRAIN_FAULT';
  String? _message;
  String? _draftState;
  bool _accessLost = false;
  bool _busy = true;
  Timer? _poll;
  String? _selectedReportId;
  late bool _history = widget.history;
  DateTime? _checkedAt;
  int _revision = 0;

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
    final revision = _revision;
    final result = await widget.api.list(widget.journey.id);
    if (!mounted || _busy || _accessLost || revision != _revision) return;
    if (result case ReportListOk(:final reports)) {
      setState(() {
        _reports = reports;
        _checkedAt = DateTime.now();
      });
    } else if (result case ReportFailure(status: 401 || 403)) {
      setState(() => _accessLost = true);
    } else if (result is ReportFailure) {
      setState(() => _checkedAt = null);
    }
  }

  Future<void> _load() async {
    final owner = await widget.journeyApi.ownerId();
    if (owner == null) {
      if (mounted) {
        setState(() {
          _busy = false;
          _accessLost = true;
          _message = 'Sign in again to use reports.';
        });
      }
      return;
    }
    final saved = await _drafts.read(owner, widget.journey.id);
    var result = await widget.api.list(widget.journey.id);
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
          } else if (lookup case ReportFailure(status: 401 || 403)) {
            result = lookup;
          }
        }
      }
    }
    if (recovered != null) await _drafts.clear(widget.journey.id);
    if (!mounted) return;
    setState(() {
      _draft = recovered == null ? saved : null;
      if (saved != null) _history = false;
      _selectedReportId = recovered?.id;
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
        _checkedAt = DateTime.now();
      } else if (result case ReportFailure(:final message, :final status)) {
        _message = message;
        _accessLost = status == 401 || status == 403;
      }
      _busy = false;
    });
  }

  Future<void> _send({bool retry = false}) async {
    if (_busy || _accessLost || !widget.canCreate || _reason == null) return;
    _revision++;
    setState(() {
      _busy = true;
      _message = null;
      _draftState = 'Sending report…';
    });
    final owner = await widget.journeyApi.ownerId();
    if (!mounted) return;
    if (owner == null) {
      setState(() {
        _busy = false;
        _draftState = 'Not sent — sign in again.';
        _accessLost = true;
      });
      return;
    }
    final draft = retry && _draft != null
        ? _draft!
        : await _drafts.create(owner, widget.journey, _reason!);
    if (!mounted) return;
    setState(() {
      _draft = draft;
      _history = false;
      _draftState = 'Sending report…';
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
        if (!mounted) return;
        setState(() {
          _draft = null;
          _accessLost = false;
          _draftState = null;
          _reports = [
            report,
            ..._reports.where((item) => item.id != report.id),
          ];
          _busy = false;
          _selectedReportId = report.id;
          _checkedAt = DateTime.now();
        });
      case ReportFailure(status: null):
        setState(() {
          _checkedAt = null;
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
    if (_busy || _accessLost || draft == null) return;
    _revision++;
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
        if (!mounted) return;
        setState(() {
          _reports = [
            reports.first,
            ..._reports.where((item) => item.id != reports.first.id),
          ];
          _draft = null;
          _draftState = null;
          _busy = false;
          _selectedReportId = reports.first.id;
          _checkedAt = DateTime.now();
        });
      case ReportListOk():
        setState(() {
          _draftState =
              'No receipt found yet. The original request may still finish.';
          _busy = false;
        });
      case ReportFailure(status: null):
        setState(() {
          _checkedAt = null;
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
    final selected = _reports
        .where((r) => r.id == _selectedReportId)
        .firstOrNull;
    if (_history && _draft == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Your reports')),
        body: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            Text('${widget.journey.trainNumber} reports'),
            if (_busy) const LinearProgressIndicator(),
            if (_message != null) Text(_message!),
            if (_accessLost)
              const Text('Access ended. Sign in again or contact Control.'),
            if (_reports.isEmpty && !_busy && _message == null && !_accessLost)
              const Text('No reports for this journey.'),
            for (final report in _reports)
              Card(
                child: ListTile(
                  minTileHeight: 64,
                  title: Text(
                    reportReasons[report.reasonCode] ?? report.reasonCode,
                  ),
                  subtitle: Text(
                    'Server received ${reportTime(report.receivedAt)}',
                  ),
                  onTap: () => setState(() {
                    _selectedReportId = report.id;
                    _history = false;
                  }),
                ),
              ),
            FilledButton(
              onPressed: _accessLost || _busy || !widget.canCreate
                  ? null
                  : () => setState(() {
                      _selectedReportId = null;
                      _history = false;
                    }),
              child: const Text('Report a problem'),
            ),
          ],
        ),
      );
    }
    // A removed report cannot silently become another receipt. Return to the list.
    if (_selectedReportId != null && selected == null && _draft == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Your reports')),
        body: Center(
          child: TextButton(
            onPressed: () => setState(() {
              _selectedReportId = null;
              _history = true;
            }),
            child: const Text(
              'This report is no longer in the current list. Back to reports',
            ),
          ),
        ),
      );
    }
    return DriverReportView(
      journey: widget.journey,
      reason: _reason ?? 'TRAIN_FAULT',
      report: selected,
      hasDraft: _draft != null,
      contextAvailable: widget.journey.label != 'details not saved',
      displayName: widget.displayName,
      now: DateTime.now(),
      checkedAt: _checkedAt,
      busy: _busy,
      notice: _accessLost
          ? 'Access ended. Sign in again or contact Control.'
          : _draftState ?? _message,
      onReason: _accessLost || _busy || _draft != null
          ? null
          : (value) => setState(() => _reason = value),
      onSend: _accessLost || _busy || !widget.canCreate
          ? null
          : () => unawaited(_send(retry: _draft != null)),
      onCheck: _accessLost || _busy
          ? null
          : _draft != null
          ? _reconcile
          : _checkStatus,
      onBack: () => Navigator.of(context).pop(),
      onReports: () => setState(() {
        _history = true;
        _selectedReportId = null;
      }),
      onAccount: widget.onAccount,
      onBriefing: widget.onBriefing,
      onUpdates: widget.onUpdates,
    );
  }

  Future<void> _checkStatus() async {
    if (_busy || _accessLost) return;
    _revision++;
    setState(() => _busy = true);
    final result = await widget.api.list(widget.journey.id);
    if (!mounted) return;
    setState(() {
      _busy = false;
      if (result case ReportListOk(:final reports)) {
        _reports = reports;
        _checkedAt = DateTime.now();
        _message = null;
      } else if (result case ReportFailure(:final status, :final message)) {
        _accessLost = status == 401 || status == 403;
        _message = message;
        _checkedAt = null;
      }
    });
  }
}

const _colomboOffset = Duration(hours: 5, minutes: 30);
DateTime reportToday([DateTime? now]) {
  final local = (now ?? DateTime.now()).toUtc().add(_colomboOffset);
  return DateTime.utc(local.year, local.month, local.day);
}
