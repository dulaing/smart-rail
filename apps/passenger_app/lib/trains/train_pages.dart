import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:smartrail_core/smartrail_core.dart';

import 'train_controller.dart';
import 'train_models.dart';
import '../l10n/app_localizations.dart';

class TrainsPage extends StatelessWidget {
  const TrainsPage({
    super.key,
    required this.controller,
    this.home = false,
    this.onShowTrains,
  });
  final TrainController controller;
  final bool home;
  final VoidCallback? onShowTrains;
  @override
  Widget build(BuildContext context) => AnimatedBuilder(
    animation: controller,
    builder: (context, _) {
      final locale = Localizations.localeOf(context).languageCode;
      final t = AppLocalizations.of(context);
      final origin = controller.station(controller.originId),
          destination = controller.station(controller.destinationId);
      final theme = Theme.of(context);
      final children = <Widget>[
        Text(
          home ? t.homeNextTrains : t.trainHeader,
          style: theme.textTheme.headlineSmall,
        ),
        const SizedBox(height: 8),
        Text(t.trainIntro, style: theme.textTheme.bodyMedium),
        const SizedBox(height: 16),
        _RoutePicker(controller: controller, locale: locale),
        const SizedBox(height: 12),
        if (controller.offline)
          _NoticeBox(
            icon: Icons.cloud_off,
            text: controller.hasSavedTimetable
                ? AppLocalizations.of(context).savedOffline
                : AppLocalizations.of(context).coldOffline,
          ),
        if (controller.liveError != null && !controller.offline)
          _NoticeBox(icon: Icons.error_outline, text: t.liveRefreshFailed),
        if (controller.partialError != null && !controller.offline)
          _NoticeBox(icon: Icons.info_outline, text: t.partialSchedule),
        if (origin != null && destination != null)
          Text(
            '${origin.name(locale)} (${origin.code})  →  ${destination.name(locale)} (${destination.code})',
            style: theme.textTheme.titleMedium,
          ),
        const SizedBox(height: 8),
        if (controller.loadingRoute && controller.liveTrains.isEmpty)
          const Center(
            child: Padding(
              padding: EdgeInsets.all(24),
              child: CircularProgressIndicator(),
            ),
          ),
        if (controller.offline &&
            controller.saved.timetableDownloadedAt != null)
          Text(
            AppLocalizations.of(context).savedDownloadedAt(
              _dateTime(
                controller.saved.timetableDownloadedAt,
                AppLocalizations.of(context).timeUnavailable,
              ),
            ),
          ),
        if (!controller.offline && controller.liveTrains.isNotEmpty) ...[
          for (final train in controller.liveTrains)
            _LiveTrainCard(
              train: train,
              controller: controller,
              locale: locale,
            ),
        ],
        if (controller.offline && controller.hasSavedTimetable) ...[
          for (final choice in controller.savedChoices())
            _SavedJourneyCard(
              choice: choice,
              controller: controller,
              locale: locale,
            ),
          if (controller.savedChoices().isEmpty)
            _NoticeBox(icon: Icons.train, text: t.noFutureTrains),
        ],
        if (!controller.loadingRoute &&
            !controller.offline &&
            controller.liveTrains.isEmpty &&
            controller.liveError == null)
          _NoticeBox(icon: Icons.train, text: t.noTrains),
        if (controller.notices.isNotEmpty) ...[
          const SizedBox(height: 12),
          Text(
            controller.offline
                ? AppLocalizations.of(context).savedNotices
                : t.confirmedChanges,
            style: theme.textTheme.titleMedium,
          ),
          for (final notice in controller.notices)
            _ChangeNotice(
              notice: notice,
              controller: controller,
              locale: locale,
              historical: controller.offline,
            ),
        ],
        const SizedBox(height: 12),
        if (home)
          Align(
            alignment: Alignment.centerLeft,
            child: OutlinedButton.icon(
              onPressed: onShowTrains,
              icon: const Icon(Icons.train),
              label: Text(t.browseAllTrains),
            ),
          ),
      ];
      return RefreshIndicator(
        onRefresh: controller.refresh,
        child: ListView(padding: const EdgeInsets.all(20), children: children),
      );
    },
  );
}

class _RoutePicker extends StatelessWidget {
  const _RoutePicker({required this.controller, required this.locale});
  final TrainController controller;
  final String locale;
  @override
  Widget build(BuildContext context) {
    if (controller.stations.isEmpty)
      return Text(
        controller.loadingStations
            ? AppLocalizations.of(context).loadingStations
            : AppLocalizations.of(context).stationsUnavailable,
      );
    final origin = controller.originId ?? controller.stations.first.id;
    final destination =
        controller.destinationId ??
        controller.stations.firstWhere((s) => s.id != origin).id;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: DropdownButtonFormField<String>(
            key: const Key('route-origin'),
            value: origin,
            isExpanded: true,
            decoration: InputDecoration(
              labelText: AppLocalizations.of(context).routeFrom,
            ),
            items: [
              for (final s in controller.stations)
                DropdownMenuItem(
                  value: s.id,
                  child: Text(
                    '${s.name(locale)} (${s.code})',
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
            ],
            onChanged: (v) {
              if (v != null && v != destination)
                controller.setRoute(v, destination);
            },
          ),
        ),
        const Padding(
          padding: EdgeInsets.symmetric(horizontal: 8, vertical: 18),
          child: Icon(Icons.arrow_forward),
        ),
        Expanded(
          child: DropdownButtonFormField<String>(
            key: const Key('route-destination'),
            value: destination,
            isExpanded: true,
            decoration: InputDecoration(
              labelText: AppLocalizations.of(context).routeTo,
            ),
            items: [
              for (final s in controller.stations)
                DropdownMenuItem(
                  value: s.id,
                  child: Text(
                    '${s.name(locale)} (${s.code})',
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
            ],
            onChanged: (v) {
              if (v != null && v != origin) controller.setRoute(origin, v);
            },
          ),
        ),
      ],
    );
  }
}

class _LiveTrainCard extends StatelessWidget {
  const _LiveTrainCard({
    required this.train,
    required this.controller,
    required this.locale,
  });
  final PassengerLiveTrain train;
  final TrainController controller;
  final String locale;
  @override
  Widget build(BuildContext context) {
    final f = controller.freshnessFor(train);
    final t = AppLocalizations.of(context);
    final dep = f == 'UNKNOWN' ? null : train.estimatedDeparture;
    final arr = f == 'UNKNOWN' ? null : train.estimatedArrival;
    final departureText =
        '${t.scheduledDeparture} ${_time(train.scheduledDeparture)}';
    final arrivalText =
        '${t.scheduledArrival} ${_time(train.scheduledArrival)}';
    final origin = controller.station(controller.originId),
        dest = controller.station(controller.destinationId);
    final p = train.confirmedPlatform ?? train.plannedPlatform;
    final platform = train.confirmedPlatform != null
        ? '${AppLocalizations.of(context).platformConfirmed} ${train.confirmedPlatform} · ${AppLocalizations.of(context).staffConfirmedChange(_dateTime(train.confirmedPlatformAt, AppLocalizations.of(context).timeUnavailable))}'
        : p == null
        ? t.platformUnavailable
        : '${t.plannedPlatform} $p';
    return Card(
      key: Key('train-${train.journeyId}'),
      margin: const EdgeInsets.symmetric(vertical: 6),
      child: InkWell(
        onTap: () => Navigator.of(context).push(
          MaterialPageRoute<void>(
            builder: (_) =>
                TrainDetailPage(train: train, controller: controller),
          ),
        ),
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      train.trainNumber,
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                  ),
                  StatusBadge.fromKey(f, locale: locale),
                ],
              ),
              const SizedBox(height: 6),
              FreshnessLine(
                observedAt: train.lastObservedAt,
                now: controller.now,
                locale: locale,
              ),
              const SizedBox(height: 8),
              Text('${origin?.code ?? ''} → ${dest?.code ?? ''}'),
              const SizedBox(height: 6),
              if (train.scheduledOriginArrival != null)
                Text(
                  '${t.scheduledArrivalOrigin} ${_time(train.scheduledOriginArrival!)}',
                ),
              Text(departureText),
              if (dep != null) Text('${t.expectedDeparture} ${_time(dep)}'),
              Text(arrivalText),
              if (arr != null) Text('${t.expectedArrival} ${_time(arr)}'),
              Text(platform),
              if (train.overdue && train.estimatedDeparture == null)
                const Text(
                  'Scheduled departure has passed; progress is unknown.',
                ),
              if (train.availability == 'ENDS_EARLY' &&
                  train.finalStopId != null)
                const Text(
                  'This train ends early. Check the final stop in train details.',
                ),
              const Align(
                alignment: Alignment.centerRight,
                child: Icon(Icons.chevron_right),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class TrainDetailPage extends StatefulWidget {
  const TrainDetailPage({
    super.key,
    required this.train,
    required this.controller,
  });
  final PassengerLiveTrain train;
  final TrainController controller;
  @override
  State<TrainDetailPage> createState() => _TrainDetailPageState();
}

class _TrainDetailPageState extends State<TrainDetailPage> {
  PassengerJourney? journey;
  String? lastKnownStationId;
  bool loading = true;
  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final results = await Future.wait<Object?>([
      widget.controller.loadJourney(
        widget.train.journeyId,
        widget.train.serviceDate,
      ),
      widget.controller.loadLastKnownStation(widget.train.journeyId),
    ]);
    journey = results[0] as PassengerJourney?;
    lastKnownStationId = results[1] as String?;
    if (mounted) setState(() => loading = false);
  }

  @override
  Widget build(BuildContext context) {
    final locale = Localizations.localeOf(context).languageCode;
    final train = widget.train;
    final c = widget.controller;
    final f = c.freshnessFor(train);
    final originIndex =
        journey?.stops.indexWhere((s) => s.id == train.originStopId) ?? -1;
    final destinationIndex =
        journey?.stops.indexWhere((s) => s.id == train.destinationStopId) ?? -1;
    final markerIndex = _lastKnownIndex(journey, lastKnownStationId);
    return Scaffold(
      appBar: AppBar(title: Text(train.trainNumber)),
      body: ListView(
        padding: const EdgeInsets.all(20),
        children: [
          Text(
            AppLocalizations.of(context).journeyDetails,
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          Text(
            '${AppLocalizations.of(context).serviceDate} ${DateFormat.yMMMd(locale).format(train.serviceDate)} · ${journey?.sourceLabel ?? AppLocalizations.of(context).scheduleSource}',
          ),
          const SizedBox(height: 12),
          StatusBadge.fromKey(f, locale: locale),
          if (train.overdue && f == 'UNKNOWN')
            _NoticeBox(
              icon: Icons.help_outline,
              text: AppLocalizations.of(context).overdueUnknown,
            ),
          if (loading)
            const Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: CircularProgressIndicator(),
              ),
            ),
          if (!loading && journey == null)
            _NoticeBox(
              icon: Icons.cloud_off,
              text: AppLocalizations.of(context).savedJourneyMissing,
            ),
          if (journey != null) ...[
            const SizedBox(height: 16),
            Text(
              'Route · scheduled stops',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            for (var i = 0; i < journey!.stops.length; i++)
              _StopRow(
                stop: journey!.stops[i],
                controller: c,
                locale: locale,
                index: i,
                originIndex: originIndex,
                destinationIndex: destinationIndex,
                markerIndex: f == 'UNKNOWN' ? -1 : markerIndex,
                finalStopIndex: train.finalStopId == null
                    ? -1
                    : journey!.stops.indexWhere(
                        (s) => s.id == train.finalStopId,
                      ),
                totalStops: journey!.stops.length,
              ),
          ],
          const SizedBox(height: 12),
          Text(
            '${AppLocalizations.of(context).timetableSource}: ${journey?.sourceLabel ?? AppLocalizations.of(context).publicRailwayTimetable}',
          ),
          Text(AppLocalizations.of(context).simulatedRunning),
        ],
      ),
    );
  }
}

class _StopRow extends StatelessWidget {
  const _StopRow({
    required this.stop,
    required this.controller,
    required this.locale,
    required this.index,
    required this.originIndex,
    required this.destinationIndex,
    required this.markerIndex,
    required this.finalStopIndex,
    required this.totalStops,
  });
  final PassengerStop stop;
  final TrainController controller;
  final String locale;
  final int index,
      originIndex,
      destinationIndex,
      markerIndex,
      finalStopIndex,
      totalStops;
  @override
  Widget build(BuildContext context) {
    final station = controller.station(stop.stationId);
    final afterTerminus = finalStopIndex >= 0 && index > finalStopIndex;
    final isRoute = index >= originIndex && index <= destinationIndex;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 32,
          child: Column(
            children: [
              Icon(
                afterTerminus
                    ? Icons.remove_circle_outline
                    : index == markerIndex
                    ? Icons.train
                    : Icons.circle,
                size: index == markerIndex ? 24 : 12,
                color: afterTerminus
                    ? Colors.grey
                    : isRoute
                    ? Theme.of(context).colorScheme.primary
                    : Colors.grey,
              ),
              if (index < totalStops - 1)
                Container(width: 2, height: 42, color: Colors.grey.shade400),
            ],
          ),
        ),
        Expanded(
          child: Padding(
            padding: const EdgeInsets.only(bottom: 14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${station?.name(locale) ?? stop.stationCode}${afterTerminus ? ' · ${AppLocalizations.of(context).notServed}' : ''}',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                Text(
                  '${AppLocalizations.of(context).arrivalShort} ${stop.arrival == null ? '—' : _time(stop.arrival!)}  ·  ${AppLocalizations.of(context).departureShort} ${stop.departure == null ? '—' : _time(stop.departure!)}',
                ),
                if (stop.platform != null)
                  Text(
                    '${AppLocalizations.of(context).plannedPlatform} ${stop.platform}',
                  ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

class _SavedJourneyCard extends StatelessWidget {
  const _SavedJourneyCard({
    required this.choice,
    required this.controller,
    required this.locale,
  });
  final OfflineTrainChoice choice;
  final TrainController controller;
  final String locale;
  @override
  Widget build(BuildContext context) {
    final journey = choice.journey;
    final origin = choice.origin;
    final dest = choice.destination;
    return Card(
      child: ListTile(
        key: Key('saved-${journey.id}'),
        title: Text(journey.trainNumber),
        subtitle: Text(
          'Scheduled departure ${_time(origin.departure!)} · scheduled arrival ${_time(dest.arrival!)}',
        ),
        trailing: const Icon(Icons.chevron_right),
        onTap: () async {
          final loaded = await controller.loadJourney(
            journey.id,
            journey.serviceDate,
          );
          if (context.mounted && loaded != null) {
            final live = PassengerLiveTrain(
              journeyId: journey.id,
              serviceDate: journey.serviceDate,
              trainNumber: journey.trainNumber,
              originStopId: origin.id,
              destinationStopId: dest.id,
              scheduledDeparture: origin.departure!,
              scheduledArrival: dest.arrival!,
              freshness: 'UNKNOWN',
              overdue: false,
              availability: 'RUNNING',
            );
            Navigator.push(
              context,
              MaterialPageRoute<void>(
                builder: (_) =>
                    TrainDetailPage(train: live, controller: controller),
              ),
            );
          }
        },
      ),
    );
  }
}

class _ChangeNotice extends StatelessWidget {
  const _ChangeNotice({
    required this.notice,
    required this.controller,
    required this.locale,
    required this.historical,
  });
  final PassengerNotice notice;
  final TrainController controller;
  final String locale;
  final bool historical;
  @override
  Widget build(BuildContext context) {
    final journey = controller.cachedJourney(notice.journeyId);
    final finalStop = notice.finalStopId == null || journey == null
        ? null
        : journey.stops.where((s) => s.id == notice.finalStopId).firstOrNull;
    final finalStation = controller.station(finalStop?.stationId);
    final t = AppLocalizations.of(context);
    final text = notice.kind == 'CANCELLED'
        ? '${notice.trainNumber} ${t.cancelledNotice}'
        : '${notice.trainNumber} ${t.endsAt}${finalStation == null ? '' : ' ${finalStation.name(locale)}'}';
    return Card(
      child: ListTile(
        leading: const Icon(Icons.warning_amber),
        title: Text(text),
        subtitle: Text(
          historical
              ? AppLocalizations.of(context).savedNoticeDetails(
                  _dateTime(
                    notice.confirmedAt,
                    AppLocalizations.of(context).timeUnavailable,
                  ),
                )
              : AppLocalizations.of(context).staffConfirmedChange(
                  _dateTime(
                    notice.confirmedAt,
                    AppLocalizations.of(context).timeUnavailable,
                  ),
                ),
        ),
      ),
    );
  }
}

class _NoticeBox extends StatelessWidget {
  const _NoticeBox({required this.icon, required this.text});
  final IconData icon;
  final String text;
  @override
  Widget build(BuildContext context) => Card(
    color: Theme.of(context).colorScheme.surfaceContainerHighest,
    child: Padding(
      padding: const EdgeInsets.all(14),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon),
          const SizedBox(width: 10),
          Expanded(child: Text(text)),
        ],
      ),
    ),
  );
}

DateTime _colomboTime(DateTime d) =>
    d.toUtc().add(const Duration(hours: 5, minutes: 30));

String _time(DateTime d) => DateFormat.Hm().format(_colomboTime(d));
String _dateTime(DateTime? d, String unavailable) => d == null
    ? unavailable
    : DateFormat.yMMMd().add_Hm().format(_colomboTime(d));
int _lastKnownIndex(PassengerJourney? journey, String? stationId) =>
    journey == null || stationId == null
    ? -1
    : journey.stops.indexWhere((stop) => stop.stationId == stationId);
