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
      final srTheme = SrTheme.of(context);
      final children = <Widget>[
        if (home) ...[
          _HomeHero(controller: controller, locale: locale),
          const SizedBox(height: 10),
          _HomeRouteCard(controller: controller, locale: locale),
          const SizedBox(height: 22),
          Row(
            children: [
              Expanded(
                child: Text(t.homeNextTrains, style: srTheme.text('heading')),
              ),
              _LiveIndicator(controller: controller),
            ],
          ),
          const SizedBox(height: 8),
        ] else ...[
          _TrainPageHeader(title: t.trainHeader, theme: srTheme),
          const SizedBox(height: 8),
          Text(t.trainIntro, style: theme.textTheme.bodyMedium),
          const SizedBox(height: 16),
          _RoutePicker(controller: controller, locale: locale),
          const SizedBox(height: 12),
        ],
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
        if (!home && origin != null && destination != null)
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
          if (home)
            _HomeLiveTrainCard(
              train: controller.liveTrains.first,
              controller: controller,
              locale: locale,
            ),
          if (home)
            for (final train in controller.liveTrains.skip(1))
              if (train.confirmedPlatform != null)
                _PlatformChangeCard(train: train),
          if (home)
            for (final train in controller.liveTrains.skip(1))
              _CompactLiveTrainCard(
                train: train,
                controller: controller,
                locale: locale,
              )
          else
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
      ];
      return RefreshIndicator(
        onRefresh: controller.refresh,
        child: ListView(
          padding: home
              ? EdgeInsets.zero
              : const EdgeInsets.fromLTRB(22, 12, 22, 20),
          children: children,
        ),
      );
    },
  );
}

class _TrainPageHeader extends StatelessWidget {
  const _TrainPageHeader({required this.title, required this.theme});
  final String title;
  final SrTheme theme;
  @override
  Widget build(BuildContext context) => Row(
    children: [
      Expanded(child: Text(title, style: theme.text('heading'))),
      Text(
        Localizations.localeOf(context).languageCode.toUpperCase(),
        style: theme.text('label'),
      ),
      const SizedBox(width: 8),
      Icon(
        Icons.notifications_none,
        color: theme.palette.textPrimary,
        size: 22,
      ),
    ],
  );
}

class _HomeHero extends StatelessWidget {
  const _HomeHero({required this.controller, required this.locale});
  final TrainController controller;
  final String locale;
  @override
  Widget build(BuildContext context) {
    final sr = SrTheme.of(context);
    final t = AppLocalizations.of(context);
    final origin = controller.station(controller.originId);
    final destination = controller.station(controller.destinationId);
    final last = controller.stations.length > 1
        ? controller.stations.last
        : null;
    final date = DateFormat(
      'EEE d MMM',
      locale,
    ).format(controller.now).toUpperCase();
    return SizedBox(
      height: 302,
      child: Stack(
        clipBehavior: Clip.hardEdge,
        children: [
          Positioned.fill(
            child: CustomPaint(
              painter: _NetworkMapPainter(
                sr.palette.bgMap,
                sr.palette.lineHairline,
                sr.palette.accentPrimary,
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(22, 4, 22, 0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    CustomPaint(
                      size: const Size(25, 20),
                      painter: _RailMarkPainter(sr.palette.accentPrimary),
                    ),
                    const SizedBox(width: 8),
                    Text(
                      t.appTitle,
                      style: sr
                          .text('body-l')
                          .copyWith(fontWeight: FontWeight.w600),
                    ),
                    const Spacer(),
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 13,
                        vertical: 8,
                      ),
                      decoration: BoxDecoration(
                        color: sr.palette.bgSurface,
                        shape: BoxShape.circle,
                        boxShadow: [
                          BoxShadow(
                            color: sr.palette.effectShadowKey.withValues(
                              alpha: .1,
                            ),
                            blurRadius: 10,
                            offset: const Offset(0, 3),
                          ),
                        ],
                      ),
                      child: Text(
                        locale.toUpperCase(),
                        style: sr.text('label'),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Container(
                      width: 38,
                      height: 38,
                      decoration: BoxDecoration(
                        color: sr.palette.bgSurface,
                        shape: BoxShape.circle,
                        boxShadow: [
                          BoxShadow(
                            color: sr.palette.effectShadowKey.withValues(
                              alpha: .1,
                            ),
                            blurRadius: 10,
                            offset: const Offset(0, 3),
                          ),
                        ],
                      ),
                      child: Icon(
                        Icons.notifications_none,
                        size: 19,
                        color: sr.palette.textPrimary,
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 18),
                Text(
                  date,
                  style: sr
                      .text('label', color: sr.palette.textSecondary)
                      .copyWith(fontSize: 10),
                ),
                const SizedBox(height: 4),
                SizedBox(
                  width: 205,
                  child: Text(
                    t.homeGreeting,
                    maxLines: 2,
                    style: sr.text('display-l').copyWith(height: 1.03),
                  ),
                ),
                const Spacer(),
                Align(
                  alignment: Alignment.centerRight,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      if (last != null)
                        Text(
                          last.name(locale),
                          style: sr.text(
                            'caption',
                            color: sr.palette.textSecondary,
                          ),
                        ),
                      if (last != null)
                        Text(last.code, style: sr.text('label')),
                    ],
                  ),
                ),
                const SizedBox(height: 10),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          if (destination != null)
                            Text(
                              destination.name(locale),
                              style: sr.text('body-l'),
                            ),
                          Text(
                            t.homeBoardHere,
                            style: sr
                                .text('label', color: sr.palette.textSecondary)
                                .copyWith(fontSize: 10),
                          ),
                        ],
                      ),
                    ),
                    if (origin != null)
                      Text(origin.name(locale), style: sr.text('body-l')),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _NetworkMapPainter extends CustomPainter {
  const _NetworkMapPainter(this.fill, this.line, this.route);
  final Color fill, line, route;
  @override
  void paint(Canvas canvas, Size size) {
    final background = Paint()..color = fill;
    canvas.drawRect(Offset.zero & size, background);
    final rail = Paint()
      ..color = line.withValues(alpha: .75)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 3;
    final path = Path()
      ..moveTo(size.width * .48, -12)
      ..lineTo(size.width * .73, size.height * .29)
      ..lineTo(size.width * .45, size.height * .58)
      ..lineTo(size.width * .16, size.height * .91);
    canvas.drawPath(path, rail);
    final branch = Path()
      ..moveTo(size.width * .77, -5)
      ..lineTo(size.width * .6, size.height * .34)
      ..lineTo(size.width * .31, size.height * .43)
      ..lineTo(size.width * .06, size.height * .8);
    canvas.drawPath(branch, rail);
    final active = Paint()
      ..color = route.withValues(alpha: .72)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 4
      ..strokeCap = StrokeCap.round;
    final journey = Path()
      ..moveTo(size.width * .8, size.height * .32)
      ..lineTo(size.width * .58, size.height * .57)
      ..quadraticBezierTo(
        size.width * .51,
        size.height * .65,
        size.width * .4,
        size.height * .65,
      )
      ..lineTo(size.width * .24, size.height * .65);
    canvas.drawPath(journey, active);
    for (final p in [
      Offset(size.width * .8, size.height * .32),
      Offset(size.width * .58, size.height * .57),
      Offset(size.width * .4, size.height * .65),
      Offset(size.width * .24, size.height * .65),
    ]) {
      canvas.drawCircle(p, 5, Paint()..color = route);
      canvas.drawCircle(p, 2.2, Paint()..color = fill);
    }
  }

  @override
  bool shouldRepaint(covariant _NetworkMapPainter old) =>
      fill != old.fill || line != old.line || route != old.route;
}

class _RailMarkPainter extends CustomPainter {
  const _RailMarkPainter(this.color);
  final Color color;
  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color
      ..strokeWidth = 2.4
      ..strokeCap = StrokeCap.round;
    canvas.drawLine(
      Offset(1, size.height * .72),
      Offset(size.width * .4, size.height * .72),
      paint,
    );
    canvas.drawLine(
      Offset(size.width * .34, size.height * .28),
      Offset(size.width * .96, size.height * .28),
      paint,
    );
    canvas.drawLine(
      Offset(size.width * .22, size.height * .5),
      Offset(size.width * .78, size.height * .5),
      paint,
    );
  }

  @override
  bool shouldRepaint(covariant _RailMarkPainter old) => color != old.color;
}

class _HomeRouteCard extends StatelessWidget {
  const _HomeRouteCard({required this.controller, required this.locale});
  final TrainController controller;
  final String locale;
  @override
  Widget build(BuildContext context) {
    final sr = SrTheme.of(context);
    if (controller.stations.isEmpty)
      return Padding(
        padding: const EdgeInsets.symmetric(horizontal: 22),
        child: Text(
          controller.loadingStations
              ? AppLocalizations.of(context).loadingStations
              : AppLocalizations.of(context).stationsUnavailable,
        ),
      );
    final origin = controller.originId ?? controller.stations.first.id;
    final destination =
        controller.destinationId ??
        controller.stations.firstWhere((s) => s.id != origin).id;
    Widget selector(
      String id,
      String label,
      String key,
      ValueChanged<String?> change,
    ) => Expanded(
      child: DropdownButtonFormField<String>(
        key: Key(key),
        value: id,
        isExpanded: true,
        icon: const SizedBox.shrink(),
        decoration: InputDecoration(
          labelText: label,
          border: InputBorder.none,
          contentPadding: EdgeInsets.zero,
          isDense: true,
        ),
        items: [
          for (final s in controller.stations)
            DropdownMenuItem(
              value: s.id,
              child: Text(
                s.name(locale),
                overflow: TextOverflow.ellipsis,
                style: sr.text('body-l'),
              ),
            ),
        ],
        onChanged: change,
      ),
    );
    return Container(
      margin: const EdgeInsets.symmetric(horizontal: 22),
      padding: const EdgeInsets.fromLTRB(14, 8, 8, 8),
      decoration: BoxDecoration(
        color: sr.palette.bgSurface,
        borderRadius: BorderRadius.circular(25),
        boxShadow: [
          BoxShadow(
            color: sr.palette.effectShadowKey.withValues(alpha: .12),
            blurRadius: 18,
            offset: const Offset(0, 6),
          ),
        ],
      ),
      child: Row(
        children: [
          Column(
            children: [
              Icon(
                Icons.radio_button_checked,
                size: 13,
                color: sr.palette.accentPrimary,
              ),
              Container(width: 1, height: 18, color: sr.palette.lineHairline),
              Icon(Icons.circle, size: 12, color: sr.palette.accentPrimary),
            ],
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              children: [
                Row(
                  children: [
                    Text(
                      AppLocalizations.of(context).routeFrom.toUpperCase(),
                      style: sr
                          .text('label', color: sr.palette.textSecondary)
                          .copyWith(fontSize: 9),
                    ),
                    const SizedBox(width: 10),
                    selector(origin, '', 'route-origin', (v) {
                      if (v != null && v != destination)
                        controller.setRoute(v, destination);
                    }),
                  ],
                ),
                Row(
                  children: [
                    Text(
                      AppLocalizations.of(context).routeTo.toUpperCase(),
                      style: sr
                          .text('label', color: sr.palette.textSecondary)
                          .copyWith(fontSize: 9),
                    ),
                    const SizedBox(width: 18),
                    selector(destination, '', 'route-destination', (v) {
                      if (v != null && v != origin)
                        controller.setRoute(origin, v);
                    }),
                  ],
                ),
              ],
            ),
          ),
          IconButton.filledTonal(
            tooltip: 'Swap route',
            onPressed: () => controller.setRoute(destination, origin),
            icon: const Icon(Icons.swap_vert),
          ),
        ],
      ),
    );
  }
}

class _LiveIndicator extends StatelessWidget {
  const _LiveIndicator({required this.controller});
  final TrainController controller;
  @override
  Widget build(BuildContext context) {
    final sr = SrTheme.of(context);
    final text = controller.offline
        ? 'Saved offline'
        : controller.observedAt == null
        ? 'Live'
        : 'Live · ${_time(controller.observedAt!)} · Simulated';
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 7,
          height: 7,
          decoration: BoxDecoration(
            color: controller.offline
                ? sr.palette.statusWarningInk
                : sr.palette.statusSuccessInk,
            shape: BoxShape.circle,
          ),
        ),
        const SizedBox(width: 5),
        Text(text, style: sr.text('caption', color: sr.palette.textSecondary)),
      ],
    );
  }
}

class _HomeLiveTrainCard extends StatelessWidget {
  const _HomeLiveTrainCard({
    required this.train,
    required this.controller,
    required this.locale,
  });
  final PassengerLiveTrain train;
  final TrainController controller;
  final String locale;
  @override
  Widget build(BuildContext context) {
    final sr = SrTheme.of(context);
    final freshness = controller.freshnessFor(train);
    final dep = freshness == 'UNKNOWN'
        ? train.scheduledDeparture
        : train.estimatedDeparture ?? train.scheduledDeparture;
    final originStop = controller
        .cachedJourney(train.journeyId)
        ?.stops
        .where((stop) => stop.id == train.originStopId)
        .firstOrNull;
    final origin = originStop == null
        ? controller.station(controller.originId)?.name(locale) ?? ''
        : controller.station(originStop.stationId)?.name(locale) ??
              originStop.stationCode;
    final destinationStop = controller
        .cachedJourney(train.journeyId)
        ?.stops
        .where((stop) => stop.id == train.destinationStopId)
        .firstOrNull;
    final destination = destinationStop == null
        ? controller.station(controller.destinationId)?.name(locale) ?? ''
        : controller.station(destinationStop.stationId)?.name(locale) ??
              destinationStop.stationCode;
    final delayMinutes = _delayMinutes(train);
    final platform = train.confirmedPlatform ?? train.plannedPlatform;
    return Container(
      margin: const EdgeInsets.symmetric(vertical: 4),
      decoration: BoxDecoration(
        color: sr.palette.bgSurface,
        borderRadius: BorderRadius.circular(22),
        boxShadow: [
          BoxShadow(
            color: sr.palette.effectShadowKey.withValues(alpha: .08),
            blurRadius: 16,
            offset: const Offset(0, 5),
          ),
        ],
      ),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: BorderRadius.circular(22),
          onTap: () => Navigator.of(context).push(
            MaterialPageRoute<void>(
              builder: (_) =>
                  TrainDetailPage(train: train, controller: controller),
            ),
          ),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(18, 14, 18, 16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Text(
                      'In ${_timeUntil(dep, controller.now)}',
                      style: sr.text('label', color: sr.palette.accentPrimary),
                    ),
                    const Spacer(),
                    StatusBadge.fromKey(
                      delayMinutes > 0 ? 'delayed' : 'on-time',
                      locale: locale,
                    ),
                  ],
                ),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            _time(dep),
                            style: sr
                                .text('number')
                                .copyWith(
                                  fontSize: 48,
                                  fontWeight: FontWeight.w300,
                                  height: 1,
                                ),
                          ),
                          Text(
                            'Expected at $origin',
                            style: sr.text(
                              'caption',
                              color: sr.palette.textSecondary,
                            ),
                          ),
                        ],
                      ),
                    ),
                    if (platform != null)
                      Column(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          Text(
                            'PLATFORM',
                            style: sr
                                .text('label', color: sr.palette.textSecondary)
                                .copyWith(fontSize: 9),
                          ),
                          Text(
                            platform,
                            style: sr
                                .text('number', color: sr.palette.accentPrimary)
                                .copyWith(fontSize: 38),
                          ),
                        ],
                      ),
                  ],
                ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    Text(
                      train.trainNumber,
                      style: sr.text('label').copyWith(fontSize: 10),
                    ),
                    const SizedBox(width: 8),
                    Expanded(child: Divider(color: sr.palette.accentPrimary)),
                    const SizedBox(width: 8),
                    Text(
                      '${destination.split(' ').last} ${_time(train.estimatedArrival ?? train.scheduledArrival)}',
                      style: sr.text(
                        'caption',
                        color: sr.palette.textSecondary,
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _CompactLiveTrainCard extends StatelessWidget {
  const _CompactLiveTrainCard({
    required this.train,
    required this.controller,
    required this.locale,
  });
  final PassengerLiveTrain train;
  final TrainController controller;
  final String locale;
  @override
  Widget build(BuildContext context) {
    final sr = SrTheme.of(context);
    final freshness = controller.freshnessFor(train);
    final departure = freshness == 'UNKNOWN'
        ? train.scheduledDeparture
        : train.estimatedDeparture ?? train.scheduledDeparture;
    final platform = train.confirmedPlatform ?? train.plannedPlatform;
    final delay = _delayMinutes(train);
    return Container(
      margin: const EdgeInsets.symmetric(vertical: 4),
      decoration: BoxDecoration(
        color: sr.palette.bgSurface,
        borderRadius: BorderRadius.circular(18),
        boxShadow: [
          BoxShadow(
            color: sr.palette.effectShadowKey.withValues(alpha: .08),
            blurRadius: 14,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(18),
        child: InkWell(
          borderRadius: BorderRadius.circular(18),
          onTap: () => Navigator.of(context).push(
            MaterialPageRoute<void>(
              builder: (_) =>
                  TrainDetailPage(train: train, controller: controller),
            ),
          ),
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 56),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
              child: Row(
                children: [
                  Text(_time(departure), style: sr.text('body-l')),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Text(train.trainNumber, style: sr.text('label')),
                        Text(
                          delay > 0
                              ? '$delay min late · sched. ${_time(train.scheduledDeparture)}'
                              : 'On time',
                          style: sr.text(
                            'caption',
                            color: delay > 0
                                ? sr.palette.statusWarningInk
                                : sr.palette.textSecondary,
                          ),
                        ),
                      ],
                    ),
                  ),
                  if (platform != null) Text(platform, style: sr.text('body')),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _PlatformChangeCard extends StatelessWidget {
  const _PlatformChangeCard({required this.train});
  final PassengerLiveTrain train;

  @override
  Widget build(BuildContext context) {
    final sr = SrTheme.of(context);
    final old = train.plannedPlatform;
    return Container(
      margin: const EdgeInsets.symmetric(vertical: 4),
      decoration: BoxDecoration(
        color: sr.palette.accentSoft,
        borderRadius: BorderRadius.circular(18),
      ),
      child: ListTile(
        minTileHeight: 52,
        leading: Icon(Icons.swap_horiz, color: sr.palette.accentPrimary),
        title: Text(
          '${train.trainNumber} now Platform ${train.confirmedPlatform}',
        ),
        subtitle: Text(
          '${old == null || old == train.confirmedPlatform ? '' : 'Was $old · '}'
          'confirmed ${train.confirmedPlatformAt == null ? 'time unavailable' : _time(train.confirmedPlatformAt!)}',
          style: sr.text('caption', color: sr.palette.textSecondary),
        ),
      ),
    );
  }
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
    final originStation = originIndex >= 0
        ? c.station(journey!.stops[originIndex].stationId)
        : null;
    final destinationStation = destinationIndex >= 0
        ? c.station(journey!.stops[destinationIndex].stationId)
        : null;
    return Scaffold(
      appBar: AppBar(
        leading: const BackButton(),
        title: Text(AppLocalizations.of(context).trainDetails),
        actions: [
          Padding(
            padding: const EdgeInsetsDirectional.only(end: 12),
            child: Center(
              child: Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 10,
                  vertical: 6,
                ),
                decoration: BoxDecoration(
                  color: SrTheme.of(context).palette.bgSurface2,
                  borderRadius: BorderRadius.circular(18),
                ),
                child: Text(
                  'SIMULATED',
                  style: SrTheme.of(context)
                      .text('label')
                      .copyWith(fontSize: 9),
                ),
              ),
            ),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(22, 10, 22, 20),
        children: [
          Row(
            children: [
              Flexible(
                child: Text(
                  '${train.trainNumber} · ${originStation?.name(locale) ?? train.originStopId}',
                  style: SrTheme.of(context).text('body-l'),
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              const Padding(
                padding: EdgeInsets.symmetric(horizontal: 6),
                child: Icon(Icons.arrow_forward, size: 16),
              ),
              Flexible(
                child: Text(
                  destinationStation?.name(locale) ?? train.destinationStopId,
                  style: SrTheme.of(context).text('body-l'),
                  overflow: TextOverflow.ellipsis,
                  textAlign: TextAlign.end,
                ),
              ),
            ],
          ),
          Text(
            'Scheduled at ${originStation?.name(locale) ?? train.originStopId} ${DateFormat.jm(locale).format(train.scheduledDeparture.add(const Duration(hours: 5, minutes: 30)))}',
            style: SrTheme.of(
              context,
            ).text('caption', color: SrTheme.of(context).palette.textSecondary),
          ),
          const SizedBox(height: 4),
          Row(
            children: [
              Container(
                width: 6,
                height: 6,
                decoration: BoxDecoration(
                  color: f == 'UNKNOWN'
                      ? SrTheme.of(context).palette.statusWarningInk
                      : SrTheme.of(context).palette.statusSuccessInk,
                  shape: BoxShape.circle,
                ),
              ),
              const SizedBox(width: 5),
              Text(
                f == 'UNKNOWN' || train.lastObservedAt == null
                    ? 'Live · observation time unavailable · Simulated'
                    : 'Live · updated ${_time(train.lastObservedAt!)} · Simulated',
                style: SrTheme.of(context).text(
                  'caption',
                  color: SrTheme.of(context).palette.textSecondary,
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          _DetailEstimate(
            train: train,
            freshness: f,
            origin: originStation?.name(locale) ?? train.originStopId,
            destination:
                destinationStation?.name(locale) ?? train.destinationStopId,
            locale: locale,
          ),
          if (train.confirmedPlatform != null)
            _DetailFact(
              icon: Icons.compare_arrows,
              title:
                  '${AppLocalizations.of(context).platformConfirmed} ${train.confirmedPlatform}',
              detail: AppLocalizations.of(context).staffConfirmedChange(
                _dateTime(
                  train.confirmedPlatformAt,
                  AppLocalizations.of(context).timeUnavailable,
                ),
              ),
            ),
          if (lastKnownStationId != null)
            _DetailFact(
              icon: Icons.location_on_outlined,
              title: 'Last known station',
              detail:
                  '${c.station(lastKnownStationId!)?.name(locale) ?? lastKnownStationId} · last seen ${train.lastObservedAt == null ? '—' : _time(train.lastObservedAt!)}',
            ),
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
            Text('Route overview', style: SrTheme.of(context).text('heading')),
            Text(
              'Station progress, not exact GPS',
              style: SrTheme.of(context).text(
                'caption',
                color: SrTheme.of(context).palette.textSecondary,
              ),
            ),
            const SizedBox(height: 10),
            _DetailRoute(
              journey: journey!,
              controller: c,
              locale: locale,
              markerIndex: f == 'UNKNOWN' ? -1 : markerIndex,
            ),
            const SizedBox(height: 12),
            Text('Scheduled stops', style: SrTheme.of(context).text('body-l')),
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

class _DetailEstimate extends StatelessWidget {
  const _DetailEstimate({
    required this.train,
    required this.freshness,
    required this.origin,
    required this.destination,
    required this.locale,
  });
  final PassengerLiveTrain train;
  final String freshness;
  final String origin;
  final String destination;
  final String locale;
  @override
  Widget build(BuildContext context) {
    final sr = SrTheme.of(context);
    final t = AppLocalizations.of(context);
    final departure = freshness == 'UNKNOWN'
        ? train.scheduledDeparture
        : train.estimatedDeparture ?? train.scheduledDeparture;
    final arrival = freshness == 'UNKNOWN'
        ? train.scheduledArrival
        : train.estimatedArrival ?? train.scheduledArrival;
    final delay = _delayMinutes(train);
    final platform = train.confirmedPlatform ?? train.plannedPlatform;
    return Container(
      margin: const EdgeInsets.only(top: 8),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: sr.palette.bgSurface,
        borderRadius: BorderRadius.circular(20),
        boxShadow: [
          BoxShadow(
            color: sr.palette.effectShadowKey.withValues(alpha: .08),
            blurRadius: 15,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (delay > 0) StatusBadge.fromKey('delayed', locale: locale),
          if (delay > 0) const SizedBox(height: 4),
          Text(
            freshness == 'UNKNOWN'
                ? t.scheduledDeparture
                : 'Expected at $origin',
            style: sr.text('body', color: sr.palette.textSecondary),
          ),
          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: Text(
                  _time(departure),
                  style: sr
                      .text('number')
                      .copyWith(
                        fontSize: 54,
                        height: 1,
                        fontWeight: FontWeight.w300,
                      ),
                ),
              ),
              if (platform != null)
                Column(
                  crossAxisAlignment: CrossAxisAlignment.end,
                  children: [
                    Text(
                      'PLATFORM',
                      style: sr.text('label').copyWith(fontSize: 9),
                    ),
                    Text(
                      platform,
                      style: sr
                          .text('number', color: sr.palette.accentPrimary)
                          .copyWith(fontSize: 38),
                    ),
                  ],
                ),
            ],
          ),
          Text(
            '${destination.split(' ').last} arrival est. ${DateFormat.jm(locale).format(arrival.add(const Duration(hours: 5, minutes: 30)))}',
            style: sr.text('body', color: sr.palette.textSecondary),
          ),
        ],
      ),
    );
  }
}

class _DetailFact extends StatelessWidget {
  const _DetailFact({
    required this.icon,
    required this.title,
    required this.detail,
  });
  final IconData icon;
  final String title;
  final String detail;
  @override
  Widget build(BuildContext context) {
    final sr = SrTheme.of(context);
    return Container(
      margin: const EdgeInsets.only(top: 8),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: sr.palette.accentSoft,
        borderRadius: BorderRadius.circular(18),
      ),
      child: Row(
        children: [
          Icon(icon, color: sr.palette.accentPrimary),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: sr.text('body-l')),
                Text(
                  detail,
                  style: sr.text('caption', color: sr.palette.textSecondary),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _DetailRoute extends StatelessWidget {
  const _DetailRoute({
    required this.journey,
    required this.controller,
    required this.locale,
    required this.markerIndex,
  });
  final PassengerJourney journey;
  final TrainController controller;
  final String locale;
  final int markerIndex;
  @override
  Widget build(BuildContext context) {
    final sr = SrTheme.of(context);
    return Container(
      padding: const EdgeInsets.all(13),
      decoration: BoxDecoration(
        color: sr.palette.bgSurface,
        borderRadius: BorderRadius.circular(18),
        boxShadow: [
          BoxShadow(
            color: sr.palette.effectShadowKey.withValues(alpha: .06),
            blurRadius: 12,
            offset: const Offset(0, 3),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              for (var i = 0; i < journey.stops.length; i++)
                Expanded(
                  child: Row(
                    children: [
                      Container(
                        width: 10,
                        height: 10,
                        decoration: BoxDecoration(
                          color: i <= markerIndex
                              ? sr.palette.accentPrimary
                              : sr.palette.bgSurface,
                          border: Border.all(
                            color: sr.palette.accentPrimary,
                            width: 2,
                          ),
                          shape: BoxShape.circle,
                        ),
                      ),
                      if (i < journey.stops.length - 1)
                        Expanded(
                          child: Container(
                            height: 2,
                            color: sr.palette.lineHairline,
                          ),
                        ),
                    ],
                  ),
                ),
            ],
          ),
          const SizedBox(height: 6),
          Row(
            children: [
              for (var i = 0; i < journey.stops.length; i++)
                Expanded(
                  child: Text(
                    controller
                            .station(journey.stops[i].stationId)
                            ?.name(locale) ??
                        journey.stops[i].stationCode,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    textAlign: i == 0
                        ? TextAlign.start
                        : i == journey.stops.length - 1
                        ? TextAlign.end
                        : TextAlign.center,
                    style: sr
                        .text('caption', color: sr.palette.textSecondary)
                        .copyWith(fontSize: 10),
                  ),
                ),
            ],
          ),
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
// "In 45 min", "In 2 h 5 min" or "In 1 d 3 h": a train tomorrow should not read as 1300 minutes.
String _timeUntil(DateTime departure, DateTime now) {
  final total = departure.toUtc().difference(now.toUtc()).inMinutes;
  final minutes = total < 0 ? 0 : total;
  final days = minutes ~/ (24 * 60);
  final hours = minutes ~/ 60 % 24;
  final mins = minutes % 60;
  return [
    if (days > 0) '$days d',
    if (hours > 0) '$hours h',
    if (mins > 0 || (days == 0 && hours == 0)) '$mins min',
  ].join(' ');
}

int _delayMinutes(PassengerLiveTrain train) {
  final estimated = train.estimatedDeparture;
  if (estimated == null) return 0;
  final minutes = estimated
      .toUtc()
      .difference(train.scheduledDeparture.toUtc())
      .inMinutes;
  return minutes > 0 ? minutes : 0;
}

String _dateTime(DateTime? d, String unavailable) => d == null
    ? unavailable
    : DateFormat.yMMMd().add_Hm().format(_colomboTime(d));
int _lastKnownIndex(PassengerJourney? journey, String? stationId) =>
    journey == null || stationId == null
    ? -1
    : journey.stops.indexWhere((stop) => stop.stationId == stationId);
