/* Link-only stubs for the offline generator. They never open USB or compress data. */
#include "../native/USBBridge.h"
#include <stdlib.h>
void *sc_usb_open_logged(sc_log_callback callback,void *context,char *error,size_t cap) { (void)callback;(void)context;(void)error;(void)cap;abort(); }
uint16_t sc_usb_product_id(void *session) { (void)session;abort(); }
void sc_usb_close(void *session) { (void)session;abort(); }
char *sc_usb_shell(void *session,const char *command,unsigned timeout,char *error,size_t cap) { (void)session;(void)command;(void)timeout;(void)error;(void)cap;abort(); }
void sc_free(void *ptr) { (void)ptr;abort(); }
uint8_t *sc_gzip(const uint8_t *data,size_t size,size_t *length) { (void)data;(void)size;(void)length;abort(); }
