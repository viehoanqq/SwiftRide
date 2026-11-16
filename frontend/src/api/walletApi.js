import client from './client';

export const walletApi = {
  getMyWallet: (signal) =>
    client.get('/wallets/me', { signal }).then(res => res.data),
};