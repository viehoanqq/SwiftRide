import client from './client';

export const tripApi = {
  create: data => client.post('/trips', data).then(response => response.data),
  getById: (id, signal) => client.get(`/trips/${id}`, { signal }).then(response => response.data),
  getDriverOffers: signal => client.get('/drivers/me/offers', { signal }).then(response => response.data),
  getDriverCurrent: signal => client.get('/drivers/me/current-trip', { signal }).then(response => (
    response.status === 204 ? null : response.data
  )),
  driverAction: (id, action) => client.post(`/trips/${id}/${action}`).then(response => response.data),
  getCurrent: signal => client.get('/trips/me/current', { signal }).then(response => (
    response.status === 204 ? null : response.data
  )),
  confirm: (id, accept, paymentMethod, cardToken = null) => {
    const payload = { accept };
    if (accept) {
      payload.paymentMethod = paymentMethod;
      if (paymentMethod === 'CARD' && cardToken) {
        payload.cardToken = cardToken;
      }
    }
    return client.post(`/trips/${id}/confirm`, payload).then(response => response.data);
  },
  approveRefund: (id, reasonCode, evidenceReference) =>
    client.post(`/trips/${id}/refund-approval`, {
      reasonCode,
      evidenceReference,
    }).then(response => response.data),
};
