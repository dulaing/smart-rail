import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../driver_frame.dart';
import 'briefing_api.dart';

/// A read-only detail of the current briefing's selected change.
class DriverUpdateDetail extends StatelessWidget {
  const DriverUpdateDetail({
    super.key,
    required this.change,
    required this.briefing,
    required this.running,
    required this.wide,
    required this.onAllUpdates,
    required this.onBriefing,
  });
  final BriefingChange change;
  final DriverBriefing briefing;
  final bool running, wide;
  final VoidCallback onAllUpdates, onBriefing;

  @override
  Widget build(BuildContext context) {
    final p = SrTheme.of(context).palette;
    final c = change, b = briefing;
    final stop = b.stops.where((s) => s.id == c.targetStopId).firstOrNull;
    final name = stop?.station ?? 'Station unavailable';
    final by = c.confirmedBy ?? 'Name unavailable';
    final confirmed = confirmationTime(c.confirmedAt);
    final platform = c.kind == 'PLATFORM', delay = c.kind == 'DELAY';
    final kind = switch (c.kind) {
      'PLATFORM' => 'PLATFORM CHANGE',
      'DELAY' => 'DELAY',
      'CANCEL' => 'CANCELLATION',
      'EARLY_END' => 'ENDS EARLY',
      _ => 'SERVICE CHANGE',
    };
    final title = platform
        ? '$name · Platform ${c.newPlatform ?? '—'}'
        : delay
        ? '${running ? 'Running' : 'Confirmed'} ${c.delayMinutes ?? '—'} min ${running ? 'late' : 'delay'}'
        : c.kind == 'CANCEL'
        ? 'This journey is cancelled'
        : 'This journey ends early';
    final scheduled = stop?.scheduledArrival ?? stop?.scheduledDeparture;
    final estimate = running
        ? stop?.estimatedArrival ?? stop?.estimatedDeparture
        : null;
    final destination = b.stops.where((s) => s.served).lastOrNull;

    Widget text(
      String value, {
      double size = 20,
      double line = 28,
      bool secondary = false,
      FontWeight weight = FontWeight.w400,
      Color? color,
    }) => DriverText(
      value,
      style: driverText(
        context,
        size,
        line,
        secondary: secondary,
        weight: weight,
        color: color,
      ),
    );
    Widget badge(String value) => Container(
      padding: EdgeInsets.symmetric(
        horizontal: wide ? 12 : 16 / 3,
        vertical: wide ? 8 : 16 / 3,
      ),
      decoration: BoxDecoration(
        color: delay ? p.statusWarningSoft : p.accentSoft,
        borderRadius: BorderRadius.circular(999),
      ),
      child: text(
        value,
        size: 14,
        line: 20,
        weight: FontWeight.w600,
        color: delay ? p.statusWarningInk : null,
      ),
    );
    Widget card(
      Widget child, {
      Key? key,
      bool accent = false,
      double? padding,
    }) => Container(
      key: key,
      padding: EdgeInsets.all(padding ?? (wide ? 32 : 16)),
      decoration: BoxDecoration(
        color: accent
            ? (delay ? p.statusWarningSoft : p.accentSoft)
            : p.bgSurface,
        borderRadius: BorderRadius.circular(wide ? 28 : 44 / 3),
      ),
      child: child,
    );
    Widget comparison(String label, String value, {bool warning = false}) =>
        Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            text(
              label,
              size: wide ? 20 : 14,
              line: wide ? 28 : 20,
              secondary: true,
              weight: wide ? FontWeight.w400 : FontWeight.w600,
            ),
            SizedBox(height: wide ? 8 : 16 / 3),
            text(
              value,
              size: wide ? 64 : 28,
              line: wide ? 76 : 36,
              weight: wide ? FontWeight.w300 : FontWeight.w600,
              color: warning ? p.statusWarningInk : null,
            ),
          ],
        );
    Widget pair(Widget first, Widget second, {double? gap, Key? key}) =>
        LayoutBuilder(
          key: key,
          builder: (context, box) {
            if (MediaQuery.textScalerOf(context).scale(20) > 28) {
              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [first, const SizedBox(height: 16), second],
              );
            }
            return Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(child: first),
                SizedBox(width: gap ?? (wide ? 32 : 32 / 3)),
                Expanded(child: second),
              ],
            );
          },
        );

    final overview = card(
      Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Wrap(
            alignment: WrapAlignment.spaceBetween,
            crossAxisAlignment: delay
                ? WrapCrossAlignment.start
                : WrapCrossAlignment.center,
            spacing: 16,
            runSpacing: 8,
            children: [
              badge('CONFIRMED · $kind'),
              KeyedSubtree(
                key: const Key('update-confirmation'),
                child: text(
                  '$by · $confirmed',
                  size: 16,
                  line: 22,
                  secondary: true,
                ),
              ),
            ],
          ),
          const SizedBox(height: 16),
          text(title, size: 36, line: 44, weight: FontWeight.w600),
          const SizedBox(height: 16),
          if (delay) ...[
            pair(
              comparison('Scheduled · $name', driverTime(scheduled)),
              comparison(
                'Expected · $name',
                driverTime(estimate),
                warning: true,
              ),
            ),
            const SizedBox(height: 16),
            text(
              '${b.trainNumber} · ${destination?.station ?? 'Destination unavailable'}${running ? ' estimated ${driverTime(destination?.estimatedArrival ?? destination?.estimatedDeparture)}' : ' · live estimate unavailable'}',
              secondary: true,
            ),
          ] else ...[
            text(
              platform
                  ? 'Was Platform ${stop?.plannedPlatform ?? '—'} · ${b.trainNumber}${running ? ' · expected ${driverTime(estimate)}' : ''}'
                  : 'Confirmed for ${b.trainNumber}',
              secondary: true,
            ),
            const SizedBox(height: 16),
            // The API supplies an issuer name, not their staff role.
            text('Confirmed by $by', weight: FontWeight.w500),
          ],
        ],
      ),
      key: const Key('update-overview'),
      accent: true,
    );
    return ListView(
      key: const Key('update-detail'),
      physics: const AlwaysScrollableScrollPhysics(),
      padding: EdgeInsets.fromLTRB(
        wide ? 32 : 55 / 3,
        wide ? 24 : 16,
        wide ? 32 : 55 / 3,
        32,
      ),
      children: wide
          ? [
              overview,
              const SizedBox(height: 24),
              card(
                Row(
                  children: [
                    const DriverAsset('shield', size: 28),
                    const SizedBox(width: 12),
                    Expanded(
                      child: text(
                        'Information only — not a signal or authority.',
                        secondary: true,
                      ),
                    ),
                  ],
                ),
                key: const Key('update-boundary'),
                padding: 16,
              ),
              const SizedBox(height: 24),
              pair(
                DriverAction(
                  label: 'View all updates',
                  onPressed: onAllUpdates,
                ),
                DriverAction(
                  label: 'Back to briefing',
                  onPressed: onBriefing,
                  outlined: true,
                ),
                gap: 24,
                key: const Key('update-actions'),
              ),
            ]
          : [
              Align(
                alignment: Alignment.centerLeft,
                child: badge('CONFIRMED BY CONTROL'),
              ),
              const SizedBox(height: 32 / 3),
              text(
                platform ? '$name platform changed' : title,
                size: 28,
                line: 36,
                weight: FontWeight.w600,
              ),
              const SizedBox(height: 32 / 3),
              card(
                Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    text('${b.trainNumber} · $name', weight: FontWeight.w500),
                    const SizedBox(height: 32 / 3),
                    pair(
                      comparison(
                        platform ? 'WAS' : 'SCHEDULED',
                        platform
                            ? stop?.plannedPlatform ?? '—'
                            : driverTime(scheduled),
                      ),
                      comparison(
                        platform ? 'NOW' : 'EXPECTED',
                        platform ? c.newPlatform ?? '—' : driverTime(estimate),
                      ),
                    ),
                    const SizedBox(height: 32 / 3),
                    text(
                      'Expected ${driverTime(estimate)} · scheduled ${driverTime(scheduled)}',
                    ),
                  ],
                ),
                key: const Key('update-overview'),
              ),
              const SizedBox(height: 32 / 3),
              card(
                Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Align(
                      alignment: Alignment.centerLeft,
                      child: DriverAsset(
                        'phone-shield',
                        key: Key('update-shield'),
                        size: 32,
                      ),
                    ),
                    const SizedBox(height: 8),
                    text(by, size: 22, line: 28, weight: FontWeight.w600),
                    const SizedBox(height: 8),
                    text('Confirmed at $confirmed', secondary: true),
                    const SizedBox(height: 8),
                    text(
                      'Applies to ${b.trainNumber} at $name.',
                      secondary: true,
                    ),
                  ],
                ),
                key: const Key('update-source'),
                padding: 40 / 3,
              ),
              const SizedBox(height: 32 / 3),
              text('Info only — follow signals.', secondary: true),
            ],
    );
  }
}

String confirmationTime(DateTime value) {
  final local = value.toUtc().add(const Duration(hours: 5, minutes: 30));
  final hour = local.hour % 12;
  return '${hour == 0 ? 12 : hour}:${local.minute.toString().padLeft(2, '0')} ${local.hour < 12 ? 'AM' : 'PM'}';
}
