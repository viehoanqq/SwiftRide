import client from './client';

export const tripApi = {
  create: data => client.post('/trips', data).then(response => response.data),
  getById: (id, signal) => client.get(`/trips/${id}`, { signal }).then(response => response.data),
  getDriverOffers: signal => client.get('/drivers/me/offers', { signal }).then(response => response.data),
  getDriverCurrent: signal => client.get('/drivers/me/current-trip', { signal }).then(response => (
    response.status === 204 ? null : response.data
  )),
  driverAction: (id, action) => client.post(`/trips/${id}/${action}`).then(response => response.data),
  getCurrent: () => client.get('/trips/me/current').then(response => (
    response.status === 204 ? null : response.data
  )),
  confirm: (id, accept, paymentMethod) => client.post(`/trips/${id}/confirm`, {
    accept,
    paymentMethod,
  }).then(response => response.data),
};
