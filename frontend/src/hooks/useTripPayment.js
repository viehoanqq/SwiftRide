import { useCallback } from 'react';
import { paymentApi } from '../api/paymentApi';
import usePolling from './usePolling';

const PAYMENT_TRIP_STATUSES = [
  'PaymentPending',
  'Paid',
  'PaymentFailed',
  'RefundPending',
  'Refunded',
  'RefundNeedsAttention',
];

export default function useTripPayment(tripId, status) {
  const active = PAYMENT_TRIP_STATUSES.includes(status);

  const load = useCallback(async signal => {
    if (!active || !tripId) return null;
    try {
      return await paymentApi.getByTripId(tripId, signal);
    } catch (err) {
      // Ngay sau dropoff, Saga đang xử lý qua RabbitMQ nên Payment có thể tạm thời trả 404.
      if (err.response?.status === 404 && status === 'PaymentPending') {
        return null;
      }
      throw err;
    }
  }, [tripId, active, status]);

  return usePolling(load, active);
}
