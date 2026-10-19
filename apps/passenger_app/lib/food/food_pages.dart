import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../l10n/app_localizations.dart';
import 'food_controller.dart';
import 'food_format.dart';
import 'food_models.dart';

/// The Food tab: public browsing from the dated train to the menu and basket. It never waits for
/// sign-in, and a food provider outage here leaves the rest of the app untouched.
class FoodPage extends StatelessWidget {
  const FoodPage({super.key, required this.controller, required this.active});

  final FoodController controller;

  /// True while Food is the visible tab, so Back steps through Food instead of leaving the app.
  final bool active;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: controller,
      builder: (context, _) {
        final inMenu = controller.selectedOption != null;
        return PopScope(
          canPop: !(active && inMenu),
          onPopInvokedWithResult: (didPop, _) {
            if (!didPop) controller.deselectOption();
          },
          child: Column(
            children: [
              Expanded(
                child: RefreshIndicator(
                  onRefresh: () async {
                    await controller.refreshOptions();
                    await controller.refreshMenu();
                  },
                  child: inMenu ? _MenuView(controller: controller) : _BrowseView(controller: controller),
                ),
              ),
              if (inMenu && controller.cartCount > 0) _BasketBar(controller: controller),
            ],
          ),
        );
      },
    );
  }
}

class _Heading extends StatelessWidget {
  const _Heading(this.text);
  final String text;

  @override
  Widget build(BuildContext context) =>
      Semantics(header: true, child: Text(text, style: SrTheme.of(context).text('title')));
}

class _Card extends StatelessWidget {
  const _Card({required this.child, this.tint});
  final Widget child;
  final Color? tint;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: tint ?? theme.palette.bgSurface,
          borderRadius: BorderRadius.circular(theme.metrics.radius['lg']!),
          border: Border.all(color: theme.palette.lineHairline),
        ),
        child: Padding(padding: const EdgeInsets.all(16), child: child),
      ),
    );
  }
}

/// A short notice with an icon; used for empty, error and provider states.
class _Notice extends StatelessWidget {
  const _Notice({required this.icon, required this.text, this.action, this.actionLabel});
  final IconData icon;
  final String text;
  final VoidCallback? action;
  final String? actionLabel;

  @override
  Widget build(BuildContext context) {
    final theme = SrTheme.of(context);
    return _Card(
      tint: theme.palette.bgSurface2,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(icon, color: theme.palette.textSecondary),
              const SizedBox(width: 12),
              Expanded(child: Text(text, style: theme.text('body'))),
            ],
          ),
          if (action != null && actionLabel != null) ...[
            const SizedBox(height: 8),
            OutlinedButton(onPressed: action, child: Text(actionLabel!)),
          ],
        ],
      ),
    );
  }
}

class _BrowseView extends StatelessWidget {
  const _BrowseView({required this.controller});
  final FoodController controller;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final locale = Localizations.localeOf(context).languageCode;
    final journey = controller.journey;
    final options = controller.options;

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(20),
      children: [
        _Heading(t.foodTitle),
        const SizedBox(height: 8),
        Text(t.foodIntro, style: theme.text('body', color: theme.palette.textSecondary)),
        const SizedBox(height: 16),
        _JourneyCard(controller: controller),
        const SizedBox(height: 4),
        if (journey != null) ...[
          Semantics(header: true, child: Text(t.foodPickupHeading, style: theme.text('heading'))),
          const SizedBox(height: 8),
          if (controller.optionsLoad == FoodLoad.loading && options == null)
            const Center(
              child: Padding(padding: EdgeInsets.all(24), child: CircularProgressIndicator()),
            ),
          if (controller.optionsLoad == FoodLoad.providerUnavailable)
            _Notice(
              icon: Icons.cloud_off_outlined,
              text: t.foodProviderUnavailable,
              action: controller.refreshOptions,
              actionLabel: t.foodRetry,
            ),
          if (controller.optionsLoad == FoodLoad.failed)
            _Notice(
              icon: Icons.error_outline,
              text: options == null ? t.foodLoadFailed : t.foodStaleOptions,
              action: controller.refreshOptions,
              actionLabel: t.foodRetry,
            ),
          if (controller.optionsLoad == FoodLoad.notFound)
            _Notice(icon: Icons.train_outlined, text: t.foodJourneyNotFound),
          if (options != null && options.options.isEmpty) _Notice(icon: Icons.storefront_outlined, text: t.foodNoPickups),
          if (options != null)
            for (final option in options.options)
              _OptionCard(
                option: option,
                locale: locale,
                onChoose: () => controller.selectOption(option),
              ),
        ],
      ],
    );
  }
}

class _JourneyCard extends StatelessWidget {
  const _JourneyCard({required this.controller});
  final FoodController controller;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final journey = controller.journey;
    return _Card(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (journey == null)
            Text(t.foodNoTrainChosen, style: theme.text('body'))
          else ...[
            Text(
              t.foodTrainLine(journey.trainNumber, journey.originCode, journey.finalCode),
              style: theme.text('heading'),
            ),
            const SizedBox(height: 4),
            Text(_dayText(t, controller, journey.serviceDate), style: theme.text('body', color: theme.palette.textSecondary)),
          ],
          const SizedBox(height: 12),
          FilledButton.tonal(
            key: const Key('food-choose-train'),
            onPressed: () => _chooseTrain(context, controller),
            child: Text(journey == null ? t.foodChooseTrain : t.foodChangeTrain),
          ),
        ],
      ),
    );
  }
}

String _dayText(AppLocalizations t, FoodController controller, DateTime serviceDate) {
  return controller.isToday(serviceDate) ? '${t.foodToday} · ${dayLabel(serviceDate)}' : dayLabel(serviceDate);
}

Future<void> _chooseTrain(BuildContext context, FoodController controller) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (sheet) => _TrainSheet(controller: controller),
  );
}

class _TrainSheet extends StatelessWidget {
  const _TrainSheet({required this.controller});
  final FoodController controller;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final choices = controller.choices();
    return DraggableScrollableSheet(
      expand: false,
      initialChildSize: 0.7,
      maxChildSize: 0.95,
      builder: (context, scroll) => ListView(
        controller: scroll,
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
        children: [
          Semantics(header: true, child: Text(t.foodChooseTrain, style: theme.text('heading'))),
          const SizedBox(height: 8),
          if (choices.isEmpty) Text(t.foodNoTrains, style: theme.text('body')),
          for (final choice in choices)
            ListTile(
              key: Key('food-train-${choice.id}'),
              contentPadding: EdgeInsets.zero,
              minVerticalPadding: 12,
              selected: controller.journey?.id == choice.id,
              title: Text(
                t.foodTrainLine(choice.trainNumber, choice.originCode, choice.finalCode),
                style: theme.text('body'),
              ),
              subtitle: Text(
                [_dayText(t, controller, choice.serviceDate), if (choice.onRoute) t.foodOnRoute].join(' · '),
                style: theme.text('caption', color: theme.palette.textSecondary),
              ),
              onTap: () {
                Navigator.of(context).pop();
                controller.selectJourney(choice);
              },
            ),
        ],
      ),
    );
  }
}

class _OptionCard extends StatelessWidget {
  const _OptionCard({required this.option, required this.locale, required this.onChoose});
  final CollectionOption option;
  final String locale;
  final VoidCallback onChoose;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final scheduled = option.scheduledArrival;
    final estimate = option.estimatedArrival;
    final secondary = theme.text('body', color: theme.palette.textSecondary);
    return _Card(
      tint: option.orderable ? null : theme.palette.bgSurface2,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(option.pointName, style: theme.text('heading')),
          const SizedBox(height: 4),
          if (estimate != null)
            Text(t.foodArrives(clockTime(estimate)), style: theme.text('body'))
          else
            Text(t.foodNoEstimate, style: theme.text('body')),
          if (scheduled != null) Text(t.foodScheduledAt(clockTime(scheduled)), style: secondary),
          if (option.orderable && option.orderBy != null) Text(t.foodOrderBy(clockTime(option.orderBy!)), style: secondary),
          const SizedBox(height: 12),
          if (option.orderable)
            FilledButton(
              key: Key('food-option-${option.stopId}'),
              onPressed: onChoose,
              child: Text(t.foodSeeMenu),
            )
          else
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.info_outline, color: theme.palette.textSecondary),
                const SizedBox(width: 8),
                Expanded(
                  child: Semantics(
                    label: '${t.foodUnavailableHeading}. ${foodReasonText(t, option.reasonCode)}',
                    excludeSemantics: true,
                    child: Text(foodReasonText(t, option.reasonCode), style: theme.text('body')),
                  ),
                ),
              ],
            ),
        ],
      ),
    );
  }
}

class _MenuView extends StatelessWidget {
  const _MenuView({required this.controller});
  final FoodController controller;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final option = controller.selectedOption!;
    final menu = controller.menu;
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(20),
      children: [
        Align(
          alignment: AlignmentDirectional.centerStart,
          child: TextButton.icon(
            key: const Key('food-back'),
            onPressed: controller.deselectOption,
            icon: const Icon(Icons.arrow_back),
            label: Text(t.foodBack),
          ),
        ),
        _Heading(t.foodMenuHeading(option.pointName)),
        const SizedBox(height: 8),
        if (!option.orderable)
          _Notice(icon: Icons.info_outline, text: t.foodPickupUnavailableNow),
        if (controller.cartAdjusted) _Notice(icon: Icons.info_outline, text: t.foodBasketAdjusted),
        if (controller.menuLoad == FoodLoad.loading && menu == null)
          const Center(
            child: Padding(padding: EdgeInsets.all(24), child: CircularProgressIndicator()),
          ),
        if (controller.menuLoad == FoodLoad.providerUnavailable)
          _Notice(
            icon: Icons.cloud_off_outlined,
            text: t.foodProviderUnavailable,
            action: controller.refreshMenu,
            actionLabel: t.foodRetry,
          ),
        if (controller.menuLoad == FoodLoad.failed)
          _Notice(
            icon: Icons.error_outline,
            text: t.foodLoadFailed,
            action: controller.refreshMenu,
            actionLabel: t.foodRetry,
          ),
        if (controller.menuLoad == FoodLoad.notFound)
          _Notice(icon: Icons.storefront_outlined, text: t.foodMenuEmpty),
        if (menu != null && menu.items.isEmpty) _Notice(icon: Icons.storefront_outlined, text: t.foodMenuEmpty),
        if (menu != null)
          for (final item in menu.items) _ItemRow(controller: controller, item: item),
      ],
    );
  }
}

class _ItemRow extends StatelessWidget {
  const _ItemRow({required this.controller, required this.item});
  final FoodController controller;
  final FoodMenuItem item;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final quantity = controller.quantityOf(item.id);
    final limit = controller.limitFor(item);
    final soldOut = limit == 0;
    return _Card(
      tint: soldOut ? theme.palette.bgSurface2 : null,
      child: Row(
        children: [
          Expanded(
            child: MergeSemantics(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(item.name, style: theme.text('heading')),
                  const SizedBox(height: 2),
                  Text(formatLkr(item.priceMinorLkr), style: theme.text('body', color: theme.palette.textSecondary)),
                  if (soldOut) Text(t.foodSoldOut, style: theme.text('body', color: theme.palette.statusDangerInk)),
                ],
              ),
            ),
          ),
          if (!soldOut) ...[
            IconButton.outlined(
              key: Key('food-remove-${item.id}'),
              tooltip: t.foodRemoveItem(item.name),
              onPressed: quantity == 0 ? null : () => controller.setQuantity(item, controller.quantityOf(item.id) - 1),
              icon: const Icon(Icons.remove),
            ),
            Semantics(
              label: t.foodQuantityOf(item.name, quantity),
              excludeSemantics: true,
              liveRegion: true,
              child: SizedBox(
                width: 36,
                child: Text('$quantity', textAlign: TextAlign.center, style: theme.text('heading')),
              ),
            ),
            IconButton.filled(
              key: Key('food-add-${item.id}'),
              tooltip: t.foodAddItem(item.name),
              onPressed: quantity >= limit ? null : () => controller.setQuantity(item, controller.quantityOf(item.id) + 1),
              icon: const Icon(Icons.add),
            ),
          ],
        ],
      ),
    );
  }
}

class _BasketBar extends StatelessWidget {
  const _BasketBar({required this.controller});
  final FoodController controller;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    return Material(
      color: theme.palette.bgSurface,
      elevation: 4,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 12),
        child: Row(
          children: [
            Expanded(
              child: Semantics(
                liveRegion: true,
                child: Text(
                  t.foodBasketSummary(controller.cartCount, formatLkr(controller.totalMinorLkr ?? 0)),
                  key: const Key('food-basket-summary'),
                  style: theme.text('heading'),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
