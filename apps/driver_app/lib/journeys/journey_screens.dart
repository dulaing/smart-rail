import 'dart:async';

import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'briefing_api.dart';
import 'briefing_cache.dart';

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
  });

  final DriverBriefingApi api;
  final String? offlineOwnerId;

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
  final BriefingCache _cache = BriefingCache();
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
                    offlineOwnerId:
                        widget.offlineOwnerId ??
                        (_usingSaved ? _ownerId : null),
                    savedRow: widget.offlineOwnerId == null
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
                  Expanded(
                    child: Text(
                      journey.trainNumber,
                      style: theme.text('heading'),
                    ),
                  ),
                  Text(journey.label, style: theme.text('body')),
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
  });

  final DriverBriefingApi api;
  final DriverJourney journey;
  final String? offlineOwnerId;
  final Map<String, dynamic>? savedRow;

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
  DateTime _deviceNow = DateTime.now();
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
      if (mounted) setState(() => _deviceNow = DateTime.now());
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
    final deviceAtResponse = DateTime.now();
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
    _deviceNow = DateTime.now();
    switch (result) {
      case DriverReadOk<DriverBriefing>(:final value, :final etag):
        final owner = await widget.api.ownerId();
        if (!mounted) return;
        if (owner != null) {
          await _cache.save(owner, widget.journey, value);
        }
        setState(() {
          _briefing = value;
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
    final theme = SrTheme.of(context);
    final locale = Localizations.localeOf(context).languageCode;
    final gap = theme.metrics.space[16]!;
    final briefing = _briefing;
    final now = _deviceNow.add(_clockOffset);
    final evidence = freshnessOf(
      observedAt: briefing?.observedAt,
      now: now,
      ageingAfterSeconds: briefing?.ageingAfterSeconds ?? 180,
      unknownAfterSeconds: briefing?.unknownAfterSeconds ?? 240,
    );
    final showEstimates = evidence != SrEvidence.unknown;

    return Scaffold(
      appBar: AppBar(
        title: Text(widget.journey.trainNumber),
        actions: widget.offlineOwnerId == null
            ? [
                IconButton(
                  tooltip: 'Refresh',
                  onPressed: () => unawaited(_load()),
                  icon: const Icon(Icons.refresh),
                ),
              ]
            : null,
      ),
      body: ListView(
        key: const Key('driver-briefing'),
        padding: EdgeInsets.all(gap),
        children: [
          if (_loading) const LinearProgressIndicator(),
          if (_downloadedAt != null)
            _Notice(
              message:
                  'Saved copy · read only · downloaded ${_time(_downloadedAt)} · last checked ${_time(_checkedAt)}',
            ),
          if (_error != null)
            _Notice(
              key: const Key('briefing-error'),
              message: _error!,
              action: _status == 401 || _status == 403
                  ? null
                  : () => unawaited(_load()),
            ),
          if (briefing != null) ...[
            Text(
              '${widget.journey.origin}  ${_time(widget.journey.departure)}  →  ${widget.journey.destination}  ${_time(widget.journey.arrival)}',
              style: theme.text('heading'),
            ),
            SizedBox(height: gap / 2),
            Text(briefing.timetableLabel, style: theme.text('body')),
            SizedBox(height: gap / 2),
            FreshnessLine(
              observedAt: briefing.observedAt,
              now: now,
              locale: locale,
              ageingAfterSeconds: briefing.ageingAfterSeconds,
              unknownAfterSeconds: briefing.unknownAfterSeconds,
            ),
            if (briefing.availability == 'CANCELLED') ...[
              SizedBox(height: gap),
              const _Notice(message: 'This journey is cancelled.'),
            ] else if (briefing.availability == 'ENDS_EARLY') ...[
              SizedBox(height: gap),
              const _Notice(
                message: 'This journey ends before its scheduled destination. Stops after its final station are not served.',
              ),
            ],
            SizedBox(height: gap),
            Text('Stops', style: theme.text('heading')),
            for (final stop in briefing.stops) ...[
              SizedBox(height: gap / 2),
              _StopRow(stop: stop, showEstimates: showEstimates, theme: theme),
            ],
            if (briefing.changes.isNotEmpty) ...[
              SizedBox(height: gap),
              Text('Confirmed changes', style: theme.text('heading')),
              for (final change in briefing.changes) ...[
                SizedBox(height: gap / 2),
                Text(_changeText(change), style: theme.text('body')),
              ],
            ],
          ],
          if (_loading && briefing == null)
            const Padding(
              padding: EdgeInsets.only(top: 24),
              child: Center(child: CircularProgressIndicator()),
            ),
        ],
      ),
    );
  }
}

class _StopRow extends StatelessWidget {
  const _StopRow({
    required this.stop,
    required this.showEstimates,
    required this.theme,
  });
  final BriefingStop stop;
  final bool showEstimates;
  final SrTheme theme;

  @override
  Widget build(BuildContext context) {
    final gap = theme.metrics.space[8]!;
    final scheduled = stop.scheduledArrival ?? stop.scheduledDeparture;
    final expected = stop.estimatedArrival ?? stop.estimatedDeparture;
    final platform = stop.confirmedPlatform ?? stop.plannedPlatform;
    return Container(
      key: Key('stop-${stop.id}'),
      padding: EdgeInsets.all(theme.metrics.space[16]!),
      decoration: BoxDecoration(
        color: theme.palette.bgSurface,
        border: Border.all(color: theme.palette.lineHairline),
        borderRadius: BorderRadius.circular(theme.metrics.radius['md']!),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text(stop.station, style: theme.text('body'))),
              Text(_time(scheduled), style: theme.text('time')),
            ],
          ),
          if (!stop.served) ...[
            SizedBox(height: gap / 2),
            Text(
              'Not served on this journey',
              style: theme.text('body', color: theme.palette.statusDangerInk),
            ),
          ],
          if (showEstimates && expected != null) ...[
            SizedBox(height: gap / 2),
            Text('Expected ${_time(expected)}', style: theme.text('body')),
          ],
          if (platform != null) ...[
            SizedBox(height: gap / 2),
            Text(
              stop.confirmedPlatform == null
                  ? 'Planned platform $platform'
                  : 'Confirmed platform $platform${stop.confirmedPlatformAt == null ? '' : ' · ${_time(stop.confirmedPlatformAt)}'}',
              style: theme.text('body'),
            ),
          ],
        ],
      ),
    );
  }
}

String _changeText(BriefingChange change) {
  final kind = switch (change.kind) {
    'DELAY' => 'Staff confirmed ${change.delayMinutes ?? 'a'} minute delay',
    'CANCEL' => 'Staff confirmed cancellation',
    'PLATFORM' => 'Staff confirmed platform ${change.newPlatform ?? 'change'}',
    'EARLY_END' => 'Staff confirmed an early ending',
    _ => 'Staff confirmed a service change',
  };
  final by = change.confirmedBy ?? 'name unavailable';
  return '$kind · $by · ${_time(change.confirmedAt)}';
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
