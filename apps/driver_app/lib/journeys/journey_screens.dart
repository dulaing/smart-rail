import 'dart:async';

import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'briefing_api.dart';
import 'briefing_cache.dart';
import 'briefing_view.dart';

const _colomboOffset = Duration(hours: 5, minutes: 30);

DateTime colomboToday([DateTime? now]) {
  final localDate = (now ?? DateTime.now()).toUtc().add(_colomboOffset);
  return DateTime.utc(localDate.year, localDate.month, localDate.day);
}

String _dateLabel(DateTime date) =>
    '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';

String _time(DateTime? value) {
  if (value == null) return '—';
  final local = value.toLocal();
  return '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
}

class DriverJourneysScreen extends StatefulWidget {
  const DriverJourneysScreen({
    super.key,
    required this.api,
    this.offlineOwnerId,
    this.cache,
    this.displayName = 'Driver',
    this.updatesOnly = false,
    this.onReport,
    this.onAccount,
    this.onReports,
  });

  final DriverBriefingApi api;
  final String? offlineOwnerId;

  /// Where saved briefings are read from; tests pass a fake, the app uses the default.
  final BriefingCache? cache;
  final String displayName;
  final bool updatesOnly;
  final void Function(DriverJourney)? onReport;
  final VoidCallback? onAccount, onReports;

  @override
  State<DriverJourneysScreen> createState() => _DriverJourneysScreenState();
}

class _DriverJourneysScreenState extends State<DriverJourneysScreen>
    with WidgetsBindingObserver {
  late DateTime _date = colomboToday();
  List<DriverJourney>? _journeys;
  String? _etag;
  String? _error;
  int? _status;
  bool _loading = true;
  bool _refreshing = false;
  Timer? _poll;
  late final BriefingCache _cache = widget.cache ?? BriefingCache();
  List<Map<String, dynamic>> _savedRows = [];
  bool _usingSaved = false;
  String? _ownerId;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (widget.offlineOwnerId == null) {
      unawaited(_load());
      _poll = Timer.periodic(
        const Duration(seconds: 30),
        (_) => unawaited(_load()),
      );
    } else {
      unawaited(_loadSaved());
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _poll?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(_load());
  }

  Future<void> _load() async {
    if (widget.offlineOwnerId != null) return _loadSaved();
    if (_refreshing) return;
    _refreshing = true;
    if (_journeys == null && mounted) setState(() => _loading = true);
    final result = await widget.api.journeys(_date, etag: _etag);
    if (!mounted) return;
    switch (result) {
      case DriverReadOk<List<DriverJourney>>(:final value, :final etag):
        // A journey saved for this date but no longer listed means its assignment was removed.
        final owner = await widget.api.ownerId();
        if (owner != null) {
          await _cache.removeUnlisted(owner, _date, {
            for (final journey in value) journey.id,
          });
        }
        if (!mounted) return;
        setState(() {
          _usingSaved = false;
          _journeys = value;
          _etag = etag;
          _error = null;
          _status = null;
          _loading = false;
        });
      case DriverReadNotModified<List<DriverJourney>>():
        setState(() {
          _usingSaved = false;
          _error = null;
          _status = null;
          _loading = false;
        });
      case DriverReadFailure<List<DriverJourney>>(
        :final statusCode,
        :final message,
      ):
        if (statusCode == 401 || statusCode == 403) {
          await _cache.clear();
        } else if (statusCode == null) {
          final owner = await widget.api.ownerId();
          if (owner != null) {
            final saved = await _cache.readAll(owner);
            final available = saved
                .where((row) => row['serviceDate'] == _dateLabel(_date))
                .map(DriverJourney.fromJson)
                .toList();
            if (available.isNotEmpty && mounted) {
              setState(() {
                _ownerId = owner;
                _usingSaved = true;
                _savedRows = saved;
                _journeys = available;
                _loading = false;
                _error = null;
              });
              _refreshing = false;
              return;
            }
          }
        }
        setState(() {
          _error = statusCode == 401 || statusCode == 403
              ? 'Your access to this journey list has changed. Sign in again or contact Control.'
              : message;
          _status = statusCode;
          _loading = false;
        });
    }
    _refreshing = false;
  }

  Future<void> _loadSaved() async {
    final owner = widget.offlineOwnerId;
    if (owner == null) return;
    final rows = await _cache.readAll(owner);
    if (!mounted) return;
    final journeys = rows
        .map((row) => DriverJourney.fromJson(row))
        .where(
          (journey) => _dateLabel(journey.serviceDate) == _dateLabel(_date),
        )
        .toList();
    setState(() {
      _ownerId = owner;
      _usingSaved = true;
      _savedRows = rows;
      _journeys = journeys;
      _loading = false;
      _error = null;
    });
  }

  void _changeDate(int days) {
    setState(() {
      _date = _date.add(Duration(days: days));
      _journeys = null;
      _etag = null;
      _error = null;
      _loading = true;
    });
    unawaited(_load());
  }

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[16]!;
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        key: const Key('driver-journeys-list'),
        padding: EdgeInsets.all(gap),
        children: [
          Row(
            children: [
              IconButton(
                tooltip: 'Previous day',
                onPressed: () => _changeDate(-1),
                icon: const Icon(Icons.chevron_left),
              ),
              Expanded(
                child: Center(
                  child: Text(_dateLabel(_date), style: theme.text('heading')),
                ),
              ),
              IconButton(
                tooltip: 'Next day',
                onPressed: () => _changeDate(1),
                icon: const Icon(Icons.chevron_right),
              ),
            ],
          ),
          if (_loading) const LinearProgressIndicator(),
          if (_error != null)
            _Notice(
              key: const Key('journey-list-error'),
              message: _error!,
              action: _status == 401 || _status == 403
                  ? null
                  : () => unawaited(_load()),
            ),
          if (_journeys != null && _journeys!.isEmpty)
            // Offline the app cannot know whether anything is assigned, so it must not say
            // "none assigned"; only a successful online read can say that.
            if (widget.offlineOwnerId != null || _usingSaved)
              const _Notice(
                key: Key('no-saved-journeys'),
                message: 'No saved briefing for this date. Reconnect to check your assignments.',
              )
            else
              const _Notice(
                key: Key('no-assigned-journeys'),
                message: 'No assigned journeys for this service date.',
              ),
          for (final journey in _journeys ?? const <DriverJourney>[]) ...[
            SizedBox(height: gap),
            _JourneyCard(
              journey: journey,
              saved: widget.offlineOwnerId != null || _usingSaved,
              onTap: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) => DriverBriefingScreen(
                    api: widget.api,
                    journey: journey,
                    displayName: widget.displayName,
                    updatesOnly: widget.updatesOnly,
                    onReport: widget.onReport == null
                        ? null
                        : () => widget.onReport!(journey),
                    onAccount: widget.onAccount,
                    onReports: widget.onReports,
                    offlineOwnerId:
                        widget.offlineOwnerId ??
                        (_usingSaved ? _ownerId : null),
                    savedRow: widget.offlineOwnerId == null && !_usingSaved
                        ? null
                        : _savedRows.firstWhere(
                            (row) => row['journeyId'] == journey.id,
                          ),
                  ),
                ),
              ),
            ),
          ],
          if (_journeys == null && !_loading && _error == null)
            const _Notice(message: 'The journey list is unavailable.'),
        ],
      ),
    );
  }
}

class _JourneyCard extends StatelessWidget {
  const _JourneyCard({
    required this.journey,
    required this.onTap,
    this.saved = false,
  });
  final DriverJourney journey;
  final VoidCallback onTap;
  final bool saved;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    final gap = theme.metrics.space[8]!;
    return Card(
      child: InkWell(
        key: Key('journey-${journey.id}'),
        onTap: onTap,
        borderRadius: BorderRadius.circular(theme.metrics.radius['md']!),
        child: Padding(
          padding: EdgeInsets.all(theme.metrics.space[16]!),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Text(journey.trainNumber, style: theme.text('heading')),
                  SizedBox(width: gap),
                  Expanded(
                    child: Text(
                      journey.label,
                      style: theme.text('body'),
                      textAlign: TextAlign.end,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                ],
              ),
              SizedBox(height: gap),
              if (saved) const Text('Saved copy · read only'),
              if (saved) SizedBox(height: gap / 2),
              Text(
                '${journey.origin}  ${_time(journey.departure)}  →  ${journey.destination}  ${_time(journey.arrival)}',
                style: theme.text('body'),
              ),
              SizedBox(height: gap),
              Text(
                journey.availability == 'CANCELLED'
                    ? 'Cancelled'
                    : journey.availability == 'ENDS_EARLY'
                    ? 'Ends early'
                    : 'As scheduled',
                style: theme.text('body'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class DriverBriefingScreen extends StatefulWidget {
  const DriverBriefingScreen({
    super.key,
    required this.api,
    required this.journey,
    this.offlineOwnerId,
    this.savedRow,
    this.displayName = 'Driver',
    this.updatesOnly = false,
    this.onReport,
    this.onAccount,
    this.onReports,
    this.now,
  });

  final DriverBriefingApi api;
  final DriverJourney journey;
  final String? offlineOwnerId;
  final Map<String, dynamic>? savedRow;
  final String displayName;
  final bool updatesOnly;
  final VoidCallback? onReport, onAccount, onReports;
  final DateTime Function()? now;

  @override
  State<DriverBriefingScreen> createState() => _DriverBriefingScreenState();
}

class _DriverBriefingScreenState extends State<DriverBriefingScreen>
    with WidgetsBindingObserver {
  DriverBriefing? _briefing;
  String? _etag;
  String? _error;
  int? _status;
  bool _loading = true;
  bool _refreshing = false;
  Duration _clockOffset = Duration.zero;
  late DateTime _deviceNow = widget.now?.call() ?? DateTime.now();
  Timer? _poll;
  Timer? _clock;
  final BriefingCache _cache = BriefingCache();
  DateTime? _downloadedAt;
  DateTime? _checkedAt;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (widget.offlineOwnerId != null && widget.savedRow != null) {
      _loadSaved(widget.savedRow!);
    } else {
      unawaited(_load());
    }
    if (widget.offlineOwnerId == null) {
      _poll = Timer.periodic(
        const Duration(seconds: 30),
        (_) => unawaited(_load()),
      );
    }
    _clock = Timer.periodic(const Duration(seconds: 15), (_) {
      if (mounted) {
        setState(() => _deviceNow = widget.now?.call() ?? DateTime.now());
      }
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _poll?.cancel();
    _clock?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(_load());
  }

  Future<void> _load() async {
    if (widget.offlineOwnerId != null) {
      return;
    }
    if (_refreshing) return;
    _refreshing = true;
    if (_briefing == null && mounted) setState(() => _loading = true);
    final deviceAtResponse = widget.now?.call() ?? DateTime.now();
    final result = await widget.api.briefing(widget.journey.id, etag: _etag);
    if (!mounted) return;
    final serverTime = switch (result) {
      DriverReadOk<DriverBriefing>(:final serverTime) => serverTime,
      DriverReadNotModified<DriverBriefing>(:final serverTime) => serverTime,
      _ => null,
    };
    if (serverTime != null) {
      _clockOffset = clockOffset(
        serverTime: serverTime,
        deviceTimeAtResponse: deviceAtResponse,
      );
    }
    _deviceNow = widget.now?.call() ?? DateTime.now();
    switch (result) {
      case DriverReadOk<DriverBriefing>(:final value, :final etag):
        final owner = await widget.api.ownerId();
        if (!mounted) return;
        if (owner != null) {
          await _cache.save(owner, widget.journey, value);
        }
        setState(() {
          _briefing = value;
          _downloadedAt = null;
          _checkedAt = null;
          _etag = etag;
          _error = null;
          _status = null;
          _loading = false;
        });
      case DriverReadNotModified<DriverBriefing>():
        final owner = await widget.api.ownerId();
        if (owner != null) await _cache.touch(owner, widget.journey.id);
        if (!mounted) return;
        _checkedAt = DateTime.now();
        setState(() {
          _error = null;
          _status = null;
          _loading = false;
        });
      case DriverReadFailure<DriverBriefing>(:final statusCode, :final message):
        if (statusCode == 401 || statusCode == 403) {
          await _cache.clear();
        } else if (statusCode == null) {
          final owner = await widget.api.ownerId();
          if (owner != null) {
            final rows = await _cache.readAll(owner);
            final saved = rows.where(
              (row) => row['journeyId'] == widget.journey.id,
            );
            if (saved.isNotEmpty) {
              _loadSaved(saved.first);
              _refreshing = false;
              return;
            }
          }
        }
        setState(() {
          _error = statusCode == 401 || statusCode == 403
              ? 'Access to this briefing is no longer available. Sign in or contact Control.'
              : message;
          _status = statusCode;
          _loading = false;
          if (statusCode == 401 || statusCode == 403) _briefing = null;
        });
    }
    _refreshing = false;
  }

  void _loadSaved(Map<String, dynamic> row) {
    final raw = row['briefing'] as Map<String, dynamic>;
    final briefing = DriverBriefing.fromJson(raw);
    setState(() {
      _briefing = briefing;
      _downloadedAt = DateTime.tryParse(row['downloadedAt'] as String? ?? '');
      _checkedAt = DateTime.tryParse(row['checkedAt'] as String? ?? '');
      _loading = false;
      _error = null;
    });
  }

  @override
  Widget build(BuildContext context) {
    final briefing = _briefing;
    if (briefing != null) {
      return DriverDutyView(
        journey: widget.journey,
        briefing: briefing,
        now: _deviceNow.add(_clockOffset),
        displayName: widget.displayName,
        updatesOnly: widget.updatesOnly,
        saved: _downloadedAt != null || widget.offlineOwnerId != null,
        downloadedAt: _downloadedAt,
        checkedAt: _checkedAt,
        notice: _error,
        onBack: () => Navigator.of(context).maybePop(),
        onAccount: widget.onAccount,
        onReport: widget.onReport,
        onReports: widget.onReports,
        onRefresh: widget.offlineOwnerId == null ? _load : null,
      );
    }
    return Scaffold(
      appBar: AppBar(title: Text(widget.journey.trainNumber)),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          if (_loading) const Center(child: CircularProgressIndicator()),
          if (_error != null)
            _Notice(
              key: const Key('briefing-error'),
              message: _error!,
              action: _status == 401 || _status == 403
                  ? null
                  : () => unawaited(_load()),
            ),
        ],
      ),
    );
  }
}

class _Notice extends StatelessWidget {
  const _Notice({super.key, required this.message, this.action});
  final String message;
  final VoidCallback? action;

  @override
  Widget build(BuildContext context) => Semantics(
    liveRegion: true,
    child: Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(message),
            if (action != null)
              Align(
                alignment: Alignment.centerLeft,
                child: TextButton(
                  onPressed: action,
                  child: const Text('Try again'),
                ),
              ),
          ],
        ),
      ),
    ),
  );
}
