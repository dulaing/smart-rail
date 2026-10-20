import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../account/account_controller.dart';
import '../l10n/app_localizations.dart';
import 'food_controller.dart';
import 'food_format.dart';
import 'food_models.dart';
import 'food_order_pages.dart';
import 'food_widgets.dart';
import 'order_models.dart';
import 'orders_controller.dart';

/// The Food tab: public browsing from the dated train to the menu and basket. It never waits for
/// sign-in, and a food provider outage here leaves the rest of the app untouched.
class FoodPage extends StatelessWidget {
  const FoodPage({
    super.key,
    required this.controller,
    required this.orders,
    required this.account,
    required this.active,
  });

  final FoodController controller;
  final FoodOrdersController orders;
  final AccountController account;

  /// True while Food is the visible tab, so Back steps through Food instead of leaving the app.
  final bool active;

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: Listenable.merge([controller, orders, account]),
      builder: (context, _) {
        final viewingOrder = orders.showingOrder;
        final inReview =
            !viewingOrder &&
            controller.reviewing &&
            controller.selectedOption != null;
        final inMenu =
            !viewingOrder && !inReview && controller.selectedOption != null;
        final Widget body = viewingOrder
            ? OrderView(orders: orders, account: account)
            : inReview
            ? ReviewView(
                controller: controller,
                orders: orders,
                account: account,
              )
            : inMenu
            ? _MenuView(controller: controller)
            : _BrowseView(controller: controller, orders: orders);
        return PopScope(
          canPop: !(active && (viewingOrder || inReview || inMenu)),
          onPopInvokedWithResult: (didPop, _) {
            if (didPop) return;
            if (viewingOrder) {
              orders.closeOrder();
            } else if (inReview) {
              controller.endReview();
            } else {
              controller.deselectOption();
            }
          },
          child: Column(
            children: [
              Expanded(
                child: RefreshIndicator(
                  onRefresh: () async {
                    await controller.refreshOptions();
                    await controller.refreshMenu();
                    await orders.refreshOrders();
                  },
                  child: body,
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

class _BrowseView extends StatelessWidget {
  const _BrowseView({required this.controller, required this.orders});
  final FoodController controller;
  final FoodOrdersController orders;

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
        FoodHeading(t.foodTitle),
        const SizedBox(height: 8),
        Text(
          t.foodIntro,
          style: theme.text('body', color: theme.palette.textSecondary),
        ),
        const SizedBox(height: 16),
        if (orders.signedIn && orders.request != null)
          FoodNotice(
            icon: Icons.hourglass_bottom,
            text: t.foodUncertainTitle,
            action: orders.openOrder,
            actionLabel: t.foodOpenOrder,
          ),
        if (orders.signedIn && orders.openOrders.isNotEmpty)
          _OrderList(
            orders: orders,
            heading: t.foodRecentOrders,
            list: orders.openOrders,
          ),
        _JourneyCard(controller: controller),
        const SizedBox(height: 4),
        if (journey != null) ...[
          Semantics(
            header: true,
            child: Text(t.foodPickupHeading, style: theme.text('heading')),
          ),
          const SizedBox(height: 8),
          if (controller.optionsLoad == FoodLoad.loading && options == null)
            const Center(
              child: Padding(
                padding: EdgeInsets.all(24),
                child: CircularProgressIndicator(),
              ),
            ),
          if (controller.optionsLoad == FoodLoad.providerUnavailable)
            FoodNotice(
              icon: Icons.cloud_off_outlined,
              text: t.foodProviderUnavailable,
              action: controller.refreshOptions,
              actionLabel: t.foodRetry,
            ),
          if (controller.optionsLoad == FoodLoad.failed)
            FoodNotice(
              icon: Icons.error_outline,
              text: options == null ? t.foodLoadFailed : t.foodStaleOptions,
              action: controller.refreshOptions,
              actionLabel: t.foodRetry,
            ),
          if (controller.optionsLoad == FoodLoad.notFound)
            FoodNotice(icon: Icons.train_outlined, text: t.foodJourneyNotFound),
          if (options != null && options.options.isEmpty)
            FoodNotice(icon: Icons.storefront_outlined, text: t.foodNoPickups),
          if (options != null)
            for (final option in options.options)
              _OptionCard(
                option: option,
                locale: locale,
                onChoose: () => controller.selectOption(option),
              ),
        ],
        if (orders.signedIn && orders.pastOrders.isNotEmpty)
          _OrderList(
            orders: orders,
            heading: t.foodOrderHistory,
            list: orders.pastOrders,
          ),
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
    return FoodCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (journey == null)
            Text(t.foodNoTrainChosen, style: theme.text('body'))
          else ...[
            Text(
              t.foodTrainLine(
                journey.trainNumber,
                journey.originCode,
                journey.finalCode,
              ),
              style: theme.text('heading'),
            ),
            const SizedBox(height: 4),
            Text(
              _dayText(t, controller, journey.serviceDate),
              style: theme.text('body', color: theme.palette.textSecondary),
            ),
          ],
          const SizedBox(height: 12),
          FilledButton.tonal(
            key: const Key('food-choose-train'),
            onPressed: () => _chooseTrain(context, controller),
            child: Text(
              journey == null ? t.foodChooseTrain : t.foodChangeTrain,
            ),
          ),
        ],
      ),
    );
  }
}

String _dayText(
  AppLocalizations t,
  FoodController controller,
  DateTime serviceDate,
) {
  return controller.isToday(serviceDate)
      ? '${t.foodToday} · ${dayLabel(serviceDate)}'
      : dayLabel(serviceDate);
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
          Semantics(
            header: true,
            child: Text(t.foodChooseTrain, style: theme.text('heading')),
          ),
          const SizedBox(height: 8),
          if (choices.isEmpty) Text(t.foodNoTrains, style: theme.text('body')),
          for (final choice in choices)
            ListTile(
              key: Key('food-train-${choice.id}'),
              contentPadding: EdgeInsets.zero,
              minVerticalPadding: 12,
              selected: controller.journey?.id == choice.id,
              title: Text(
                t.foodTrainLine(
                  choice.trainNumber,
                  choice.originCode,
                  choice.finalCode,
                ),
                style: theme.text('body'),
              ),
              subtitle: Text(
                [
                  _dayText(t, controller, choice.serviceDate),
                  if (choice.onRoute) t.foodOnRoute,
                ].join(' · '),
                style: theme.text(
                  'caption',
                  color: theme.palette.textSecondary,
                ),
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
  const _OptionCard({
    required this.option,
    required this.locale,
    required this.onChoose,
  });
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
    return FoodCard(
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
          if (scheduled != null)
            Text(t.foodScheduledAt(clockTime(scheduled)), style: secondary),
          if (option.orderable && option.orderBy != null)
            Text(t.foodOrderBy(clockTime(option.orderBy!)), style: secondary),
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
                    label:
                        '${t.foodUnavailableHeading}. ${foodReasonText(t, option.reasonCode)}',
                    excludeSemantics: true,
                    child: Text(
                      foodReasonText(t, option.reasonCode),
                      style: theme.text('body'),
                    ),
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
      key: const Key('food-menu-scroll'),
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.fromLTRB(22, 14, 22, 20),
      children: [
        Wrap(
          alignment: WrapAlignment.spaceBetween,
          crossAxisAlignment: WrapCrossAlignment.center,
          runSpacing: 4,
          children: [
            Text(
              'RailBites',
              style: SrTheme.of(context).text('heading').copyWith(fontSize: 24),
            ),
            const _OpenPill(label: 'SIMULATED DATA'),
          ],
        ),
        const SizedBox(height: 12),
        Text(
          'Station pickup · open',
          style: SrTheme.of(
            context,
          ).text('caption', color: SrTheme.of(context).palette.textSecondary),
        ),
        const SizedBox(height: 30),
        if (controller.journey != null)
          FoodCard(
            padding: const EdgeInsets.all(12),
            child: Row(
              children: [
                Icon(
                  Icons.storefront_outlined,
                  color: SrTheme.of(context).palette.accentPrimary,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        t.foodCollectAt(option.pointName),
                        style: SrTheme.of(context).text('body'),
                      ),
                      Text(
                        [
                          controller.journey!.trainNumber,
                          if (option.estimatedArrival != null)
                            clockTime(option.estimatedArrival!),
                          if (option.orderBy != null)
                            t.foodOrderBy(clockTime(option.orderBy!)),
                        ].join(' / '),
                        style: SrTheme.of(context).text(
                          'caption',
                          color: SrTheme.of(context).palette.textSecondary,
                        ),
                      ),
                    ],
                  ),
                ),
                const Icon(Icons.chevron_right),
              ],
            ),
          ),
        if (!option.orderable)
          FoodNotice(
            icon: Icons.info_outline,
            text: t.foodPickupUnavailableNow,
          ),
        if (controller.cartAdjusted)
          FoodNotice(icon: Icons.info_outline, text: t.foodBasketAdjusted),
        if (controller.menuLoad == FoodLoad.loading && menu == null)
          const Center(
            child: Padding(
              padding: EdgeInsets.all(24),
              child: CircularProgressIndicator(),
            ),
          ),
        if (controller.menuLoad == FoodLoad.providerUnavailable)
          FoodNotice(
            icon: Icons.cloud_off_outlined,
            text: t.foodProviderUnavailable,
            action: controller.refreshMenu,
            actionLabel: t.foodRetry,
          ),
        if (controller.menuLoad == FoodLoad.failed)
          FoodNotice(
            icon: Icons.error_outline,
            text: t.foodLoadFailed,
            action: controller.refreshMenu,
            actionLabel: t.foodRetry,
          ),
        if (controller.menuLoad == FoodLoad.notFound)
          FoodNotice(icon: Icons.storefront_outlined, text: t.foodMenuEmpty),
        if (menu != null && menu.items.isEmpty)
          FoodNotice(icon: Icons.storefront_outlined, text: t.foodMenuEmpty),
        if (menu != null && menu.items.isNotEmpty)
          LayoutBuilder(
            builder: (context, constraints) {
              const spacing = 10.0;
              final columns = MediaQuery.textScalerOf(context).scale(1) >= 1.5
                  ? 1
                  : 2;
              final width =
                  (constraints.maxWidth - spacing * (columns - 1)) / columns;
              return Wrap(
                spacing: spacing,
                runSpacing: 10,
                children: [
                  for (final item in menu.items)
                    SizedBox(
                      width: width,
                      child: ConstrainedBox(
                        constraints: BoxConstraints(
                          minHeight: columns == 2 ? 220 : 0,
                        ),
                        child: _ItemRow(controller: controller, item: item),
                      ),
                    ),
                ],
              );
            },
          ),
        if (controller.cartCount > 0) _BasketBar(controller: controller),
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
    final asset = _foodPhoto(item.name);
    return Padding(
      padding: EdgeInsets.zero,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: soldOut ? theme.palette.bgSurface2 : theme.palette.bgSurface,
          borderRadius: BorderRadius.circular(18),
          boxShadow: [
            BoxShadow(
              color: theme.palette.effectShadowKey.withValues(alpha: .08),
              blurRadius: 12,
              offset: const Offset(0, 4),
            ),
          ],
        ),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(18),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (asset != null)
                Image.asset(
                  asset,
                  height: 110,
                  fit: BoxFit.cover,
                  semanticLabel: item.name,
                ),
              Padding(
                padding: const EdgeInsets.fromLTRB(8, 8, 8, 10),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      item.name,
                      style: theme
                          .text('body')
                          .copyWith(fontSize: 14, fontWeight: FontWeight.w600),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      formatLkr(item.priceMinorLkr),
                      style: theme
                          .text('caption', color: theme.palette.textSecondary)
                          .copyWith(fontSize: 12),
                    ),
                    if (soldOut)
                      Padding(
                        padding: const EdgeInsets.only(top: 4),
                        child: Text(
                          t.foodSoldOut,
                          style: theme
                              .text(
                                'caption',
                                color: theme.palette.textSecondary,
                              )
                              .copyWith(
                                fontSize: 10,
                                fontWeight: FontWeight.w600,
                              ),
                        ),
                      )
                    else
                      Row(
                        mainAxisAlignment: MainAxisAlignment.spaceBetween,
                        children: [
                          _QuantityButton(
                            controlKey: Key('food-remove-${item.id}'),
                            tooltip: t.foodRemoveItem(item.name),
                            icon: Icons.remove,
                            filled: false,
                            onPressed: quantity == 0
                                ? null
                                : () => controller.setQuantity(
                                    item,
                                    controller.quantityOf(item.id) - 1,
                                  ),
                          ),
                          Semantics(
                            label: t.foodQuantityOf(item.name, quantity),
                            excludeSemantics: true,
                            liveRegion: true,
                            child: Text(
                              '$quantity',
                              style: theme
                                  .text('body')
                                  .copyWith(fontWeight: FontWeight.w600),
                            ),
                          ),
                          _QuantityButton(
                            controlKey: Key('food-add-${item.id}'),
                            tooltip: t.foodAddItem(item.name),
                            icon: Icons.add,
                            filled: true,
                            onPressed: quantity >= limit
                                ? null
                                : () => controller.setQuantity(
                                    item,
                                    controller.quantityOf(item.id) + 1,
                                  ),
                          ),
                        ],
                      ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

String? _foodPhoto(String name) {
  final normalized = name.toLowerCase();
  if (normalized.contains('rice')) return 'assets/food/rice-curry.png';
  if (normalized.contains('tea')) return 'assets/food/plain-tea.png';
  if (normalized.contains('bun')) return 'assets/food/fish-bun.png';
  if (normalized.contains('roti')) return 'assets/food/egg-roti.png';
  return null;
}

class _QuantityButton extends StatelessWidget {
  const _QuantityButton({
    required this.controlKey,
    required this.tooltip,
    required this.icon,
    required this.filled,
    required this.onPressed,
  });
  final Key controlKey;
  final String tooltip;
  final IconData icon;
  final bool filled;
  final VoidCallback? onPressed;
  @override
  Widget build(BuildContext context) {
    final palette = SrTheme.of(context).palette;
    final color = filled ? palette.textPrimary : palette.bgSurface2;
    final ink = filled ? palette.bgCanvas : palette.textPrimary;
    return Semantics(
      key: controlKey,
      button: true,
      enabled: onPressed != null,
      label: tooltip,
      tooltip: tooltip,
      onTap: onPressed,
      child: ExcludeSemantics(
        child: Material(
          color: Colors.transparent,
          child: InkWell(
            customBorder: const CircleBorder(),
            onTap: onPressed,
            child: SizedBox(
              width: 48,
              height: 48,
              child: Center(
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: color,
                    shape: BoxShape.circle,
                  ),
                  child: SizedBox(
                    width: 32,
                    height: 32,
                    child: Icon(icon, size: 18, color: ink),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _OpenPill extends StatelessWidget {
  const _OpenPill({required this.label});
  final String label;
  @override
  Widget build(BuildContext context) => DecoratedBox(
    decoration: BoxDecoration(
      color: SrTheme.of(context).palette.bgMap,
      borderRadius: BorderRadius.circular(20),
    ),
    child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
      child: Text(
        label,
        style: SrTheme.of(context)
            .text('caption')
            .copyWith(fontSize: 10, fontWeight: FontWeight.w600),
      ),
    ),
  );
}

class _BasketBar extends StatelessWidget {
  const _BasketBar({required this.controller});
  final FoodController controller;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final largeText = MediaQuery.textScalerOf(context).scale(1) >= 1.5;
    final summary = t.foodBasketSummary(
      controller.cartCount,
      formatLkr(controller.totalMinorLkr ?? 0),
    );
    return Material(
      color: theme.palette.bgCanvas,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(0, 12, 0, 8),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            FilledButton(
              key: const Key('food-review'),
              style: FilledButton.styleFrom(
                minimumSize: const Size.fromHeight(52),
              ),
              onPressed: controller.canReview ? controller.startReview : null,
              child: Semantics(
                liveRegion: true,
                child: Text(
                  largeText
                      ? t.foodPlaceOrder
                      : '${t.foodPlaceOrder} · $summary',
                  key: largeText ? null : const Key('food-basket-summary'),
                  textAlign: TextAlign.center,
                ),
              ),
            ),
            if (largeText)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  summary,
                  key: largeText ? const Key('food-basket-summary') : null,
                  style: theme.text(
                    'caption',
                    color: theme.palette.textSecondary,
                  ),
                ),
              )
            else
              const SizedBox(height: 4),
            Text(
              t.foodPayAtPickup,
              textAlign: TextAlign.start,
              style: theme.text('caption', color: theme.palette.textSecondary),
            ),
          ],
        ),
      ),
    );
  }
}

/// The account's own recent orders, each opening its pickup code and status.
class _OrderList extends StatelessWidget {
  const _OrderList({
    required this.orders,
    required this.heading,
    required this.list,
  });
  final FoodOrdersController orders;
  final String heading;
  final List<FoodOrder> list;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Semantics(
          header: true,
          child: Text(heading, style: theme.text('heading')),
        ),
        const SizedBox(height: 8),
        for (final order in list.take(5))
          FoodCard(
            child: InkWell(
              key: Key('food-recent-${order.id}'),
              onTap: () => orders.select(order.id),
              child: ConstrainedBox(
                constraints: BoxConstraints(minHeight: theme.metrics.minTarget),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      [
                        for (final i in order.items ?? const <FoodOrderItem>[])
                          '${i.quantity} × ${i.name}',
                      ].join(', '),
                      style: theme.text('body'),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      '${orderStatusText(t, order.status)} · ${t.foodOrderPlacedAt(clockTime(order.createdAt))}',
                      style: theme.text(
                        'body',
                        color: theme.palette.textSecondary,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        const SizedBox(height: 4),
      ],
    );
  }
}
