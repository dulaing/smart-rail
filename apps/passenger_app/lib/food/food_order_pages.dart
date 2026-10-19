import 'package:flutter/material.dart';
import 'package:smartrail_core/smartrail_core.dart';

import '../account/account_controller.dart';
import '../l10n/app_localizations.dart';
import 'food_controller.dart';
import 'food_format.dart';
import 'food_models.dart';
import 'food_widgets.dart';
import 'order_models.dart';
import 'orders_controller.dart';

/// The last step before submitting: what, where, when, how it is paid, and the rules. A guest sees
/// the same page with a sign-in button instead of Place order.
class ReviewView extends StatelessWidget {
  const ReviewView({super.key, required this.controller, required this.orders, required this.account});

  final FoodController controller;
  final FoodOrdersController orders;
  final AccountController account;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final option = controller.selectedOption;
    final journey = controller.journey;
    final secondary = theme.text('body', color: theme.palette.textSecondary);
    final estimate = option?.estimatedArrival;

    final Widget action;
    if (!orders.signedIn) {
      action = Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(t.foodSignInNote, style: secondary),
          if (account.signInFailed) ...[
            const SizedBox(height: 8),
            Text(t.foodSignInCancelled, style: theme.text('body')),
          ],
          const SizedBox(height: 12),
          FilledButton(
            key: const Key('food-sign-in'),
            onPressed: account.busy ? null : account.signIn,
            child: Text(t.foodSignInToOrder),
          ),
        ],
      );
    } else if (!orders.mayOrder) {
      action = Text(t.foodNoPermission, style: theme.text('body'));
    } else {
      action = FilledButton(
        key: const Key('food-place-order'),
        onPressed: controller.canReview && orders.phase == OrderPhase.none && orders.request == null
            ? () => orders.submit(controller.buildDraft()!, onCommitted: controller.finishOrder)
            : null,
        child: Text(t.foodPlaceOrder),
      );
    }

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(20),
      children: [
        Align(
          alignment: AlignmentDirectional.centerStart,
          child: TextButton.icon(
            key: const Key('food-review-back'),
            onPressed: controller.endReview,
            icon: const Icon(Icons.arrow_back),
            label: Text(t.foodBackToMenu),
          ),
        ),
        FoodHeading(t.foodReviewHeading),
        const SizedBox(height: 12),
        if (option != null && !option.orderable)
          FoodNotice(icon: Icons.info_outline, text: foodReasonText(t, option.reasonCode)),
        if (controller.cartAdjusted) FoodNotice(icon: Icons.info_outline, text: t.foodBasketAdjusted),
        if (orders.request != null)
          FoodNotice(
            icon: Icons.hourglass_bottom,
            text: t.foodPendingOrderWaiting,
            action: orders.openOrder,
            actionLabel: t.foodOpenOrder,
          ),
        FoodCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (journey != null) ...[
                Text(
                  t.foodTrainLine(journey.trainNumber, journey.originCode, journey.finalCode),
                  style: theme.text('heading'),
                ),
                Text(
                  controller.isToday(journey.serviceDate)
                      ? '${t.foodToday} · ${dayLabel(journey.serviceDate)}'
                      : dayLabel(journey.serviceDate),
                  style: secondary,
                ),
                const SizedBox(height: 8),
              ],
              if (option != null) Text(t.foodCollectAt(option.pointName), style: theme.text('body')),
              if (estimate != null) Text(t.foodTrainExpected(clockTime(estimate)), style: secondary),
              if (option?.orderBy != null) Text(t.foodOrderBy(clockTime(option!.orderBy!)), style: secondary),
            ],
          ),
        ),
        FoodCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              for (final (item, quantity) in controller.cartLines)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(child: Text('$quantity × ${item.name}', style: theme.text('body'))),
                      const SizedBox(width: 12),
                      Text(formatLkr(item.priceMinorLkr * quantity), style: theme.text('body')),
                    ],
                  ),
                ),
              const Divider(),
              Row(
                children: [
                  Expanded(child: Text(t.foodTotal, style: theme.text('heading'))),
                  Text(
                    formatLkr(controller.totalMinorLkr ?? 0),
                    key: const Key('food-review-total'),
                    style: theme.text('heading'),
                  ),
                ],
              ),
            ],
          ),
        ),
        FoodCard(
          tint: theme.palette.bgSurface2,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(t.foodPayAtKiosk, style: theme.text('body')),
              const SizedBox(height: 8),
              Text(t.foodNoShowPolicy, style: theme.text('body')),
              const SizedBox(height: 8),
              Text(t.foodCancelPolicy, style: theme.text('body')),
            ],
          ),
        ),
        action,
      ],
    );
  }
}

String orderStatusText(AppLocalizations t, String status) => switch (status) {
  OrderStatus.placing => t.foodStatusPlacing,
  OrderStatus.rejected => t.foodStatusRejected,
  OrderStatus.accepted => t.foodStatusAccepted,
  OrderStatus.ready => t.foodStatusReady,
  OrderStatus.collected => t.foodStatusCollected,
  OrderStatus.noShow => t.foodStatusNoShow,
  OrderStatus.cancelled => t.foodStatusCancelled,
  _ => status,
};

/// What happens after Place order: waiting, an unknown outcome, a refusal, or the confirmed order
/// with its pickup code. Only this signed-in view ever shows a pickup code.
class OrderView extends StatelessWidget {
  const OrderView({super.key, required this.orders, required this.account});

  final FoodOrdersController orders;
  final AccountController account;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    final order = orders.order;
    final children = <Widget>[];

    switch (orders.phase) {
      case OrderPhase.submitting:
        children.addAll([
          FoodHeading(t.foodYourOrder),
          const SizedBox(height: 16),
          Row(
            children: [
              const SizedBox(width: 24, height: 24, child: CircularProgressIndicator(strokeWidth: 3)),
              const SizedBox(width: 12),
              Expanded(child: Text(t.foodSubmitting, style: theme.text('body'))),
            ],
          ),
        ]);
      case OrderPhase.uncertain:
        children.addAll(_uncertain(context, t, theme));
      case OrderPhase.conflict:
        children.addAll([
          FoodHeading(t.foodYourOrder),
          const SizedBox(height: 12),
          FoodNotice(icon: Icons.warning_amber_outlined, text: t.foodConflictBody),
          OutlinedButton(
            key: const Key('food-discard-request'),
            onPressed: orders.discardConflict,
            child: Text(t.foodDiscardRequest),
          ),
        ]);
      case OrderPhase.refused:
        children.addAll([
          FoodHeading(t.foodRefusedTitle),
          const SizedBox(height: 12),
          FoodNotice(icon: Icons.info_outline, text: foodReasonText(t, orders.refusalCode)),
          FilledButton(
            key: const Key('food-refused-back'),
            onPressed: orders.dismissProblem,
            child: Text(t.foodRefusedBack),
          ),
        ]);
      case OrderPhase.sessionEnded:
        children.addAll([
          FoodHeading(t.foodYourOrder),
          const SizedBox(height: 12),
          FoodNotice(icon: Icons.lock_outline, text: t.foodSessionEnded),
          FilledButton(
            key: const Key('food-sign-in-again'),
            onPressed: account.busy ? null : account.signIn,
            child: Text(t.signIn),
          ),
        ]);
      case OrderPhase.forbidden:
        children.addAll([
          FoodHeading(t.foodYourOrder),
          const SizedBox(height: 12),
          FoodNotice(icon: Icons.block, text: t.foodNoPermission),
          FilledButton(onPressed: orders.dismissProblem, child: Text(t.foodRefusedBack)),
        ]);
      case OrderPhase.storageFailed:
        children.addAll([
          FoodHeading(t.foodYourOrder),
          const SizedBox(height: 12),
          FoodNotice(icon: Icons.error_outline, text: t.foodStorageFailed),
          FilledButton(onPressed: orders.dismissProblem, child: Text(t.foodRefusedBack)),
        ]);
      case OrderPhase.none:
        if (order != null) children.addAll(_confirmed(context, t, theme, order));
    }

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      padding: const EdgeInsets.all(20),
      children: [
        Align(
          alignment: AlignmentDirectional.centerStart,
          child: TextButton.icon(
            key: const Key('food-order-back'),
            onPressed: orders.closeOrder,
            icon: const Icon(Icons.arrow_back),
            label: Text(t.foodBackToFood),
          ),
        ),
        ...children,
      ],
    );
  }

  List<Widget> _uncertain(BuildContext context, AppLocalizations t, SrTheme theme) {
    final request = orders.request;
    return [
      Semantics(
        liveRegion: true,
        header: true,
        child: Text(t.foodUncertainTitle, style: theme.text('title'), key: const Key('food-uncertain-title')),
      ),
      const SizedBox(height: 12),
      Text(t.foodUncertainBody, style: theme.text('body')),
      const SizedBox(height: 12),
      if (request != null) _SummaryCard(display: request.display),
      FilledButton(
        key: const Key('food-check-again'),
        onPressed: orders.checking ? null : orders.recover,
        child: Text(orders.checking ? t.foodChecking : t.foodCheckAgain),
      ),
    ];
  }

  Future<void> _confirmCancel(BuildContext context, AppLocalizations t) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialog) => AlertDialog(
        title: Text(t.foodCancelConfirmTitle),
        content: Text(t.foodCancelConfirmBody),
        actions: [
          TextButton(
            key: const Key('food-keep-order'),
            onPressed: () => Navigator.of(dialog).pop(false),
            child: Text(t.foodKeepOrder),
          ),
          TextButton(
            key: const Key('food-confirm-cancel'),
            onPressed: () => Navigator.of(dialog).pop(true),
            child: Text(t.foodCancelOrder),
          ),
        ],
      ),
    );
    if (confirmed == true) await orders.cancel();
  }

  List<Widget> _confirmed(BuildContext context, AppLocalizations t, SrTheme theme, FoodOrder order) {
    final secondary = theme.text('body', color: theme.palette.textSecondary);
    final open = !order.isTerminal;
    final pickup = order.pickupTime;
    final estimate = pickup?.estimatedArrival;
    final evidence = pickup?.etaEvidenceAt;
    return [
      Semantics(header: true, child: Text(t.foodOrderConfirmed, style: theme.text('title'))),
      const SizedBox(height: 12),
      FoodCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Semantics(
              liveRegion: true,
              child: Text(
                orderStatusText(t, order.status),
                key: const Key('food-order-status'),
                style: theme.text('heading'),
              ),
            ),
            if (order.reasonCode != null && (order.status == OrderStatus.cancelled || order.status == OrderStatus.rejected)) ...[
              const SizedBox(height: 4),
              Text(foodReasonText(t, order.reasonCode), style: theme.text('body')),
            ],
            if (open && order.pickupCode != null) ...[
              const SizedBox(height: 12),
              Text(t.foodPickupCode, style: secondary),
              Semantics(
                label: '${t.foodPickupCode}: ${order.pickupCode!.split('').join(' ')}',
                excludeSemantics: true,
                child: Text(
                  order.pickupCode!,
                  key: const Key('food-pickup-code'),
                  style: theme.text('title').copyWith(letterSpacing: 4),
                ),
              ),
              Text(t.foodPickupCodeHint, style: secondary),
            ],
            const SizedBox(height: 12),
            if (estimate != null) Text(t.foodArrives(clockTime(estimate)), style: theme.text('body')),
            if (estimate == null && pickup != null) Text(t.foodNoEstimate, style: theme.text('body')),
            if (evidence != null) Text(t.foodEvidenceAt(clockTime(evidence)), style: secondary),
          ],
        ),
      ),
      if (order.items != null)
        FoodCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              for (final item in order.items!)
                Padding(
                  padding: const EdgeInsets.only(bottom: 8),
                  child: Row(
                    children: [
                      Expanded(child: Text('${item.quantity} × ${item.name}', style: theme.text('body'))),
                      Text(formatLkr(item.quantity * item.unitPriceMinorLkr), style: theme.text('body')),
                    ],
                  ),
                ),
              if (order.totalMinorLkr != null) ...[
                const Divider(),
                Row(
                  children: [
                    Expanded(child: Text(t.foodTotal, style: theme.text('heading'))),
                    Text(formatLkr(order.totalMinorLkr!), style: theme.text('heading')),
                  ],
                ),
              ],
            ],
          ),
        ),
      FoodCard(tint: theme.palette.bgSurface2, child: Text(t.foodPayAtKiosk, style: theme.text('body'))),
      if (orders.cancelProblem != null)
        FoodNotice(
          icon: Icons.info_outline,
          text: switch (orders.cancelProblem) {
            'PROVIDER_UNAVAILABLE' => t.foodCancelUnavailable,
            'UNCONFIRMED' => t.foodCancelUnconfirmed,
            final code => foodReasonText(t, code),
          },
        ),
      if (open)
        OutlinedButton(
          key: const Key('food-cancel-order'),
          onPressed: orders.cancelling ? null : () => _confirmCancel(context, t),
          child: Text(orders.cancelling ? t.foodCancelling : t.foodCancelOrder),
        ),
      const SizedBox(height: 8),
      FilledButton(key: const Key('food-order-done'), onPressed: orders.closeOrder, child: Text(t.foodOrderDone)),
    ];
  }
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.display});
  final OrderDisplay display;

  @override
  Widget build(BuildContext context) {
    final t = AppLocalizations.of(context);
    final theme = SrTheme.of(context);
    return FoodCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(display.pointName, style: theme.text('heading')),
          Text(display.trainNumber, style: theme.text('body', color: theme.palette.textSecondary)),
          const SizedBox(height: 8),
          for (final line in display.lines)
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: Row(
                children: [
                  Expanded(child: Text('${line.quantity} × ${line.name}', style: theme.text('body'))),
                  Text(formatLkr(line.quantity * line.unitPriceMinorLkr), style: theme.text('body')),
                ],
              ),
            ),
          const Divider(),
          Row(
            children: [
              Expanded(child: Text(t.foodTotal, style: theme.text('heading'))),
              Text(formatLkr(display.totalMinorLkr), style: theme.text('heading')),
            ],
          ),
        ],
      ),
    );
  }
}
