/* Local libusb substitute: this executable never links libusb or accesses hardware. */
#include "USBBridge.h"
#include <libusb.h>
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static unsigned char stream[4096],request[257],golden[257];
static size_t stream_n,cursor;static int scenario,sent,claim_error,matches=1,endpoints=3,releases,read_calls;static char logs[65536];
static unsigned cookie;static int tests,context_initializations,context_exits;static uint16_t product_id=9;
static unsigned short checksum(const unsigned char *p,size_t n){unsigned short c=0;while(n--){c^=(unsigned short)*p++<<8;for(int i=0;i<8;i++)c=c&0x8000?(c<<1)^0x1021:c<<1;}return c;}
static void put(unsigned char *p,unsigned n){for(int i=0;i<4;i++)p[i]=n>>(8*i);}
static unsigned get(const unsigned char *p){return p[0]|(unsigned)p[1]<<8|(unsigned)p[2]<<16|(unsigned)p[3]<<24;}
static void frame(unsigned char *p,unsigned command,unsigned function,unsigned token,unsigned status,const char *text){memset(p,0,257);memcpy(p,"\x09\0\x05\x08\xfc",5);put(p+5,command);put(p+9,function);put(p+13,token);put(p+21,status);memcpy(p+25,text,strlen(text));put(p+17,checksum(p+21,236));}
static void trace(const char *line,void *context){(void)context;if(strlen(logs)+strlen(line)+2<sizeof(logs)){strcat(logs,line);strcat(logs,"\n");}}
int libusb_init(libusb_context **ctx){context_initializations++;*ctx=(libusb_context *)1;return 0;}
void libusb_exit(libusb_context *ctx){(void)ctx;context_exits++;}
ssize_t libusb_get_device_list(libusb_context *ctx,libusb_device ***list){(void)ctx;static libusb_device *devices[3]={(libusb_device *)2,(libusb_device *)3,NULL};*list=devices;return matches;}
void libusb_free_device_list(libusb_device **list,int unref){(void)list;(void)unref;}
int libusb_get_device_descriptor(libusb_device *d,struct libusb_device_descriptor *desc){memset(desc,0,sizeof(*desc));desc->idVendor=0x2756;desc->idProduct=scenario==18&&d==(libusb_device *)3?10:product_id;return 0;}
int libusb_open(libusb_device *d,libusb_device_handle **h){(void)d;*h=(libusb_device_handle *)4;return 0;}
void libusb_close(libusb_device_handle *h){(void)h;}
int libusb_claim_interface(libusb_device_handle *h,int i){(void)h;assert(i==3);return claim_error;}
int libusb_release_interface(libusb_device_handle *h,int i){(void)h;assert(i==3);releases++;return 0;}
int libusb_set_interface_alt_setting(libusb_device_handle *h,int i,int alt){(void)h;assert(i==3&&alt==0);return 0;}
libusb_device *libusb_get_device(libusb_device_handle *h){(void)h;return (libusb_device *)2;}
int libusb_get_active_config_descriptor(libusb_device *d,struct libusb_config_descriptor **config){
 (void)d;static struct libusb_endpoint_descriptor ep[2];static struct libusb_interface_descriptor alt;static struct libusb_interface interface;static struct libusb_config_descriptor c;
 memset(ep,0,sizeof(ep));ep[0].bEndpointAddress=endpoints&1?4:3;ep[1].bEndpointAddress=endpoints&2?0x85:0x86;ep[0].bmAttributes=ep[1].bmAttributes=2;
 memset(&alt,0,sizeof(alt));alt.bInterfaceNumber=3;alt.bNumEndpoints=2;alt.endpoint=ep;interface.altsetting=&alt;interface.num_altsetting=1;memset(&c,0,sizeof(c));c.bNumInterfaces=1;c.interface=&interface;*config=&c;return 0;
}
void libusb_free_config_descriptor(struct libusb_config_descriptor *config){(void)config;}
const char *libusb_error_name(int rc){switch(rc){case -4:return "LIBUSB_ERROR_NO_DEVICE";case -6:return "LIBUSB_ERROR_BUSY";case -7:return "LIBUSB_ERROR_TIMEOUT";case -9:return "LIBUSB_ERROR_PIPE";case -8:return "LIBUSB_ERROR_OVERFLOW";default:return "LIBUSB_ERROR_IO";}}
int libusb_bulk_transfer(libusb_device_handle *h,unsigned char endpoint,unsigned char *data,int length,int *transferred,unsigned timeout){
 (void)h;(void)timeout;*transferred=0;
 if(endpoint==4){assert(length==257);memcpy(request,data,257);cookie=get(data+13);sent=1;cursor=0;stream_n=0;
  unsigned char expected[257];memcpy(expected,golden,257);put(expected+13,cookie);assert(!memcmp(expected,request,257));
  if(scenario==8){*transferred=256;return 0;}if(scenario==9)return -9;
  frame(stream,65,4,cookie,0,"eagle2_ec1706_native\n");frame(stream+257,65,0,cookie,scenario==7?7:0,"");stream_n=514;
  if(scenario==3){memmove(stream+7,stream,stream_n);memcpy(stream,"\x09\0\x05\x08\xfc\xff\xff",7);stream_n+=7;}
  if(scenario==4){memmove(stream+514,stream,stream_n);frame(stream,65,4,cookie+1,0,"foreign");frame(stream+257,49,0,cookie,0,"");stream_n+=514;}
  if(scenario==5)stream_n=257;if(scenario==11){stream[40]^=1;stream_n=257;}
  if(scenario==0||scenario==1)stream_n=1024;
  *transferred=257;return 0;
 }
 assert(endpoint==0x85&&length==1024);read_calls++;
 if(!sent)return -7;
 if(scenario==6)return -4;if(scenario==10)return -9;
 if(scenario==12){char text[233];memset(text,'x',232);text[232]=0;frame(data,65,4,cookie,0,text);*transferred=257;return 0;}
 if(cursor>=stream_n)return -7;
 size_t n=stream_n-cursor;if(n>(size_t)length)n=length;if(scenario==2&&n>17)n=17;
 memcpy(data,stream+cursor,n);cursor+=n;*transferred=(int)n;return scenario==1?-7:0;
}
static void reset(int mode){memset(stream,0,sizeof(stream));scenario=mode;sent=0;stream_n=cursor=0;logs[0]=0;claim_error=0;matches=1;endpoints=3;releases=read_calls=0;product_id=9;}
void sc_test_initialize(const char *path){FILE *f=fopen(path,"rb");assert(f&&fread(golden,1,257,f)==257);fclose(f);}
void sc_test_scenario(int mode){reset(mode);if(mode==13)matches=0;if(mode==14)claim_error=-6;if(mode==15)endpoints=1;}
#ifndef SC_TEST_EMBED
static void run(int mode,const char *name,int success,const char *diagnostic){
 reset(mode);char error[512]={0};void *session=sc_usb_open_logged(trace,NULL,error,sizeof(error));assert(session);char *output=sc_usb_shell(session,"getprop ro.product.device",mode==12?5000:10,error,sizeof(error));
 if(success){assert(output&&!strcmp(output,"eagle2_ec1706_native\n"));assert(strstr(logs,diagnostic));sc_free(output);}else{assert(!output&&strstr(error,diagnostic));}
 sc_usb_close(session);assert(releases==1);assert(read_calls>0);assert(context_initializations==1&&context_exits==0);tests++;printf("PASS %s\n",name);
}
int main(int argc,char **argv){assert(argc==2);sc_test_initialize(argv[1]);
 run(0,"toolkit-1024-byte-padded-response",1,"完成");run(1,"timeout-with-received-data",1,"超时带数据=1");run(2,"fragmented-header-and-frames",1,"完成");run(3,"bad-crc-byte-resynchronization",1,"CRC异常=1");run(4,"foreign-cookie-and-command-ignored",1,"其他请求=2");
 run(5,"notification-without-final-is-not-success",0,"LIBUSB_ERROR_TIMEOUT");run(6,"device-disconnected-code-preserved",0,"LIBUSB_ERROR_NO_DEVICE");run(7,"command-status-preserved",0,"状态 7");run(8,"short-request-write",0,"请求发送");run(9,"write-stall-code-preserved",0,"LIBUSB_ERROR_PIPE");run(10,"read-stall-code-preserved",0,"LIBUSB_ERROR_PIPE");run(11,"crc-only-corruption-never-succeeds",0,"LIBUSB_ERROR_TIMEOUT");run(12,"output-size-limit",0,"LIBUSB_ERROR_OVERFLOW");
 char error[512];reset(0);matches=0;assert(!sc_usb_open_logged(trace,NULL,error,sizeof(error)));assert(strstr(error,"未检测到"));tests++;
 reset(0);matches=2;assert(!sc_usb_open_logged(trace,NULL,error,sizeof(error)));assert(strstr(error,"只连接一台"));tests++;
 reset(0);claim_error=-6;assert(!sc_usb_open_logged(trace,NULL,error,sizeof(error)));assert(strstr(error,"LIBUSB_ERROR_BUSY")&&releases==0);tests++;
 reset(0);endpoints=1;assert(!sc_usb_open_logged(trace,NULL,error,sizeof(error)));assert(strstr(error,"端点检查")&&releases==1);tests++;
 reset(0);product_id=10;void *cfv=sc_usb_open_logged(trace,NULL,error,sizeof(error));assert(cfv&&sc_usb_product_id(cfv)==10);sc_usb_close(cfv);assert(releases==1);tests++;puts("PASS cfv100-usb-product-selected");
 reset(0);product_id=8;assert(!sc_usb_open_logged(trace,NULL,error,sizeof(error)));assert(strstr(error,"未检测到")&&releases==0);tests++;puts("PASS other-usb-product-refused");
 reset(18);matches=2;assert(!sc_usb_open_logged(trace,NULL,error,sizeof(error)));assert(strstr(error,"只连接一台")&&releases==0);tests++;puts("PASS mixed-x2d-cfv-connection-refused");
 printf("PASS: %d transport cases; no libusb linked; no hardware accessed\n",tests);return 0;
}

#endif
