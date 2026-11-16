import client from './client';

export const walletApi = {
  getMyWallet: (signal) =>
    client.get('/api/wallet', { signal }).then(res => res.data),
};