import client from './client';

export const paymentApi = {
  getByTripId: (tripId, signal) =>
    client.get(`/api/payments/trip/${tripId}`, { signal }).then(res => res.data),
};