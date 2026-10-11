#include <stdint.h>
#include <stddef.h>
typedef void (*sc_log_callback)(const char *message, void *context);
void *sc_usb_open_logged(sc_log_callback log, void *context, char *error, size_t capacity);
void *sc_usb_open(char *error, size_t capacity);
void sc_usb_close(void *session);
uint16_t sc_usb_product_id(void *session);
char *sc_usb_shell(void *session, const char *command, unsigned timeout_ms, char *error, size_t capacity);
void sc_free(void *p);
uint8_t *sc_gzip(const uint8_t *data, size_t size, size_t *result_size);
