import client from './client';

export const authApi = {
  register: data => client.post('/auth/register', data).then(res => res.data),
  login: (phone, password) => client.post('/auth/login', { phone, password }).then(res => res.data),
  me: () => client.get('/auth/me').then(res => res.data),
};
